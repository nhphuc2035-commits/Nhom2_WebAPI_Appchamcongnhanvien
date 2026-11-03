using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using WebAPI_DiemDanh.Data;
using WebAPI_DiemDanh.Helpers;
using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Repositories
{
    public class ReportRepository : IReportRepository
    {
        private static readonly CultureInfo Vi = new("vi-VN");
        private const string NoDepartment = "Chưa phân ban";

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReportRepository(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        /// <summary>Nhân sự cần chấm công = mọi tài khoản không phải Admin.</summary>
        private async Task<List<ApplicationUser>> LoadStaffAsync(bool activeOnly, string? department = null, string? userId = null)
        {
            var adminIds = (await _userManager.GetUsersInRoleAsync(UserRoles.Admin)).Select(u => u.Id).ToHashSet();
            var q = _userManager.Users.AsNoTracking().AsQueryable();
            if (activeOnly) q = q.Where(u => u.IsActive);
            if (!string.IsNullOrWhiteSpace(department)) q = q.Where(u => u.Department == department);
            if (!string.IsNullOrWhiteSpace(userId)) q = q.Where(u => u.Id == userId);
            return (await q.OrderBy(u => u.FullName).ToListAsync()).Where(u => !adminIds.Contains(u.Id)).ToList();
        }

        private async Task<Dictionary<string, List<SalaryConfig>>> LoadConfigsAsync(List<string> ids)
        {
            var configs = await _context.SalaryConfigs.AsNoTracking().Where(s => ids.Contains(s.UserId)).ToListAsync();
            return configs.GroupBy(c => c.UserId).ToDictionary(g => g.Key, g => g.ToList());
        }

        private static string Dept(ApplicationUser? u) => string.IsNullOrWhiteSpace(u?.Department) ? NoDepartment : u!.Department!;

        // =====================================================================
        // DASHBOARD
        // =====================================================================
        public async Task<ApiResponse<DashboardSummaryModel>> GetDashboardAsync()
        {
            var now = VnTime.Now;
            var today = now.Date;
            var monthPeriod = PeriodHelper.Resolve(PeriodHelper.Month, today.Year, today.Month, null, null);
            var from14 = today.AddDays(-13);
            var rangeStart = from14 < monthPeriod.From ? from14 : monthPeriod.From;

            var allStaff = await LoadStaffAsync(activeOnly: false);
            var active = allStaff.Where(u => u.IsActive).ToList();
            var staffIds = allStaff.Select(u => u.Id).ToHashSet();
            var managerIds = (await _userManager.GetUsersInRoleAsync(UserRoles.Manager)).Select(u => u.Id).ToHashSet();

            var records = await _context.AttendanceRecords.AsNoTracking()
                .Where(a => a.AttendanceDate >= rangeStart && a.AttendanceDate <= today)
                .ToListAsync();
            records = records.Where(r => staffIds.Contains(r.UserId)).ToList();

            var missingCheckouts = await _context.AttendanceRecords.AsNoTracking()
                .CountAsync(a => a.CheckOutTime == null && a.AttendanceDate < today);

            var todayRecords = records.Where(r => r.AttendanceDate == today).ToList();
            var todayByUser = todayRecords.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            var monthRecords = records.Where(r => r.AttendanceDate >= monthPeriod.From && r.AttendanceDate <= monthPeriod.To).ToList();
            var configs = await LoadConfigsAsync(allStaff.Select(u => u.Id).ToList());

            decimal payroll = 0;
            double otHours = 0;
            foreach (var u in allStaff)
            {
                var userRecs = monthRecords.Where(r => r.UserId == u.Id).ToList();
                if (userRecs.Count == 0) continue;
                var calc = SalaryCalculator.Calculate(u,
                    configs.TryGetValue(u.Id, out var cs) ? SalaryCalculator.PickConfig(cs, monthPeriod.To) : null,
                    userRecs, monthPeriod);
                payroll += calc.TotalSalary;
                otHours += calc.OvertimeHours;
            }

            var userLookup = allStaff.ToDictionary(u => u.Id);

            var last14 = Enumerable.Range(0, 14)
                .Select(i => from14.AddDays(i))
                .Select(d =>
                {
                    var dayRecs = records.Where(r => r.AttendanceDate == d).ToList();
                    return new DashboardDayPoint
                    {
                        Date = d,
                        Label = $"{d:dd/MM}",
                        CheckIns = dayRecs.Select(r => r.UserId).Distinct().Count(),
                        Hours = Math.Round(dayRecs.Sum(r => r.TotalWorkingHours), 2)
                    };
                }).ToList();

            var departments = active
                .GroupBy(Dept)
                .Select(g =>
                {
                    var ids = g.Select(u => u.Id).ToHashSet();
                    var recs = monthRecords.Where(r => ids.Contains(r.UserId)).ToList();
                    var hours = Math.Round(recs.Sum(r => r.TotalWorkingHours), 2);
                    return new DepartmentAttendanceSummary
                    {
                        Department = g.Key,
                        TotalEmployees = ids.Count,
                        TotalHoursWorked = hours,
                        TotalCheckIns = recs.Count(r => r.CheckInTime.HasValue),
                        AverageHoursPerEmployee = ids.Count > 0 ? Math.Round(hours / ids.Count, 2) : 0
                    };
                })
                .OrderByDescending(d => d.TotalHoursWorked)
                .ToList();

            var todayAttendance = active.Select(u =>
                {
                    todayByUser.TryGetValue(u.Id, out var r);
                    return new TodayEmployeeStatus
                    {
                        UserId = u.Id,
                        FullName = u.FullName,
                        Department = u.Department,
                        Position = u.Position,
                        CheckInTime = r?.CheckInTime,
                        CheckOutTime = r?.CheckOutTime,
                        Hours = r?.TotalWorkingHours ?? 0,
                        IsManual = r?.IsManualEntry ?? false,
                        Status = r == null ? AttendanceStatuses.NotCheckedIn
                            : AttendanceRepository.ComputeStatus(r, today)
                    };
                })
                .OrderBy(s => s.Status == AttendanceStatuses.Working ? 0 : s.Status == AttendanceStatuses.Completed ? 1 : 2)
                .ThenBy(s => s.CheckInTime)
                .ThenBy(s => s.FullName)
                .ToList();

            var recentRaw = await _context.AttendanceRecords.Include(a => a.User).AsNoTracking()
                .OrderByDescending(a => a.UpdatedAt ?? a.CreatedAt)
                .Take(10)
                .ToListAsync();

            var dto = new DashboardSummaryModel
            {
                ServerTime = now,
                MonthLabel = monthPeriod.Label,
                TotalEmployees = allStaff.Count,
                ActiveEmployees = active.Count,
                LockedEmployees = allStaff.Count - active.Count,
                ManagerCount = allStaff.Count(u => managerIds.Contains(u.Id)),
                BiometricEnrolledCount = active.Count(u => !string.IsNullOrEmpty(u.BiometricKey)),
                CheckedInToday = todayAttendance.Count(t => t.Status != AttendanceStatuses.NotCheckedIn),
                CurrentlyWorking = todayAttendance.Count(t => t.Status == AttendanceStatuses.Working),
                CompletedToday = todayAttendance.Count(t => t.Status == AttendanceStatuses.Completed),
                NotCheckedInToday = todayAttendance.Count(t => t.Status == AttendanceStatuses.NotCheckedIn),
                MissingCheckouts = missingCheckouts,
                ManualEntriesThisMonth = monthRecords.Count(r => r.IsManualEntry),
                HoursThisMonth = Math.Round(monthRecords.Sum(r => r.TotalWorkingHours), 2),
                OvertimeHoursThisMonth = Math.Round(otHours, 2),
                PayrollThisMonth = payroll,
                Last14Days = last14,
                Departments = departments,
                TodayAttendance = todayAttendance,
                RecentActivities = recentRaw.Select(a => AttendanceRepository.Map(a, a.User, null)).ToList()
            };

            return ApiResponse<DashboardSummaryModel>.Ok(dto, "Lấy dữ liệu tổng quan thành công.");
        }

        // =====================================================================
        // BÁO CÁO TỔNG HỢP THEO KỲ (Tuần / Tháng / Quý / Năm)
        // =====================================================================
        public async Task<ApiResponse<ReportOverviewModel>> GetOverviewAsync(ReportFilterModel filter)
        {
            var period = PeriodHelper.Resolve(filter.PeriodType, filter.Year, filter.Month, filter.Quarter, filter.WeekNumber, filter.FromDate, filter.ToDate);
            var staff = await LoadStaffAsync(activeOnly: false, filter.Department, filter.UserId);
            var ids = staff.Select(u => u.Id).ToList();
            var idSet = ids.ToHashSet();

            var records = (await _context.AttendanceRecords.AsNoTracking()
                    .Where(a => a.AttendanceDate >= period.From && a.AttendanceDate <= period.To)
                    .ToListAsync())
                .Where(r => idSet.Contains(r.UserId))
                .ToList();

            var configs = await LoadConfigsAsync(ids);
            var today = VnTime.Today;

            // Chỉ hiển thị: nhân viên đang hoạt động + nhân viên đã khóa nhưng có dữ liệu trong kỳ
            var employees = staff
                .Where(u => u.IsActive || records.Any(r => r.UserId == u.Id))
                .Select(u =>
                {
                    var recs = records.Where(r => r.UserId == u.Id).ToList();
                    var calc = SalaryCalculator.Calculate(u,
                        configs.TryGetValue(u.Id, out var cs) ? SalaryCalculator.PickConfig(cs, period.To) : null,
                        recs, period);
                    return new EmployeePeriodSummary
                    {
                        UserId = u.Id,
                        FullName = u.FullName,
                        Username = u.UserName,
                        Email = u.Email ?? string.Empty,
                        Department = u.Department,
                        Position = u.Position,
                        DaysPresent = recs.Where(r => r.CheckInTime.HasValue).Select(r => r.AttendanceDate).Distinct().Count(),
                        TotalHours = calc.TotalWorkingHours,
                        RegularHours = calc.RegularHours,
                        OvertimeHours = calc.OvertimeHours,
                        ManualEntries = recs.Count(r => r.IsManualEntry),
                        MissingCheckouts = recs.Count(r => r.CheckOutTime == null && r.AttendanceDate < today),
                        EstimatedSalary = calc.TotalSalary
                    };
                })
                .OrderByDescending(e => e.TotalHours)
                .ToList();

            var userLookup = staff.ToDictionary(u => u.Id);
            var departments = employees
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Department) ? NoDepartment : e.Department!)
                .Select(g =>
                {
                    var hours = Math.Round(g.Sum(e => e.TotalHours), 2);
                    var count = g.Count();
                    return new DepartmentAttendanceSummary
                    {
                        Department = g.Key,
                        TotalEmployees = count,
                        TotalHoursWorked = hours,
                        TotalCheckIns = g.Sum(e => e.DaysPresent),
                        AverageHoursPerEmployee = count > 0 ? Math.Round(hours / count, 2) : 0
                    };
                })
                .OrderByDescending(d => d.TotalHoursWorked)
                .ToList();

            var totalHours = Math.Round(records.Sum(r => r.TotalWorkingHours), 2);
            var withAttendance = employees.Count(e => e.DaysPresent > 0);

            var dto = new ReportOverviewModel
            {
                PeriodType = period.PeriodType,
                PeriodLabel = period.Label,
                FromDate = period.From,
                ToDate = period.To,
                TotalEmployees = employees.Count,
                EmployeesWithAttendance = withAttendance,
                TotalDaysPresent = employees.Sum(e => e.DaysPresent),
                TotalHours = totalHours,
                TotalOvertimeHours = Math.Round(employees.Sum(e => e.OvertimeHours), 2),
                TotalManualEntries = records.Count(r => r.IsManualEntry),
                TotalMissingCheckouts = employees.Sum(e => e.MissingCheckouts),
                AverageHoursPerEmployee = withAttendance > 0 ? Math.Round(totalHours / withAttendance, 2) : 0,
                EstimatedPayroll = employees.Sum(e => e.EstimatedSalary),
                Timeline = BuildTimeline(records, period),
                Departments = departments,
                Employees = employees
            };

            return ApiResponse<ReportOverviewModel>.Ok(dto, $"Tổng hợp báo cáo {period.Label} thành công.");
        }

        public async Task<ApiResponse<List<PeriodicAttendanceSummary>>> GetStatisticsReportAsync(ReportFilterModel filter)
        {
            var period = PeriodHelper.Resolve(filter.PeriodType, filter.Year, filter.Month, filter.Quarter, filter.WeekNumber, filter.FromDate, filter.ToDate);
            var staff = await LoadStaffAsync(activeOnly: false, filter.Department, filter.UserId);
            var idSet = staff.Select(u => u.Id).ToHashSet();

            var records = (await _context.AttendanceRecords.AsNoTracking()
                    .Where(a => a.AttendanceDate >= period.From && a.AttendanceDate <= period.To)
                    .ToListAsync())
                .Where(r => idSet.Contains(r.UserId))
                .ToList();

            return ApiResponse<List<PeriodicAttendanceSummary>>.Ok(BuildTimeline(records, period), $"Tổng hợp thống kê {period.Label} thành công.");
        }

        /// <summary>
        /// Chia nhỏ kỳ thành các mốc biểu đồ (đủ cả những mốc không có dữ liệu):
        /// Tuần → 7 ngày | Tháng → từng ngày | Quý → từng tuần | Năm → 12 tháng | Tùy chọn → từng ngày (≤ 62 ngày) hoặc từng tháng.
        /// </summary>
        private static List<PeriodicAttendanceSummary> BuildTimeline(List<AttendanceRecord> records, PeriodRange period)
        {
            var buckets = new List<(string Key, string Label, DateTime From, DateTime To)>();
            var totalDays = (period.To - period.From).Days + 1;

            if (period.PeriodType == PeriodHelper.Year || (period.PeriodType == PeriodHelper.Custom && totalDays > 62))
            {
                var cursor = new DateTime(period.From.Year, period.From.Month, 1);
                while (cursor <= period.To)
                {
                    var end = cursor.AddMonths(1).AddDays(-1);
                    buckets.Add(($"{cursor:yyyy-MM}", $"T{cursor.Month}" + (period.From.Year != period.To.Year ? $"/{cursor:yy}" : ""),
                        cursor < period.From ? period.From : cursor, end > period.To ? period.To : end));
                    cursor = cursor.AddMonths(1);
                }
            }
            else if (period.PeriodType == PeriodHelper.Quarter)
            {
                var cursor = period.From;
                while (cursor <= period.To)
                {
                    var weekStart = cursor.AddDays(-(((int)cursor.DayOfWeek + 6) % 7));
                    var weekEnd = weekStart.AddDays(6);
                    var f = weekStart < period.From ? period.From : weekStart;
                    var t = weekEnd > period.To ? period.To : weekEnd;
                    buckets.Add(($"W{ISOWeek.GetWeekOfYear(f)}", $"Tuần {ISOWeek.GetWeekOfYear(f)}", f, t));
                    cursor = weekEnd.AddDays(1);
                }
            }
            else
            {
                for (var d = period.From; d <= period.To; d = d.AddDays(1))
                {
                    var label = period.PeriodType == PeriodHelper.Week
                        ? $"{Capitalize(d.ToString("ddd", Vi))} {d:dd/MM}"
                        : $"{d:dd/MM}";
                    buckets.Add(($"{d:yyyy-MM-dd}", label, d, d));
                }
            }

            return buckets.Select(b =>
            {
                var recs = records.Where(r => r.AttendanceDate >= b.From && r.AttendanceDate <= b.To).ToList();
                var employees = recs.Select(r => r.UserId).Distinct().Count();
                var hours = Math.Round(recs.Sum(r => r.TotalWorkingHours), 2);
                return new PeriodicAttendanceSummary
                {
                    PeriodKey = b.Key,
                    PeriodLabel = b.Label,
                    TotalHours = hours,
                    TotalCheckIns = recs.Count(r => r.CheckInTime.HasValue),
                    TotalManualEntries = recs.Count(r => r.IsManualEntry),
                    ActiveEmployeesCount = employees,
                    AverageHoursPerEmployee = employees > 0 ? Math.Round(hours / employees, 2) : 0
                };
            }).ToList();
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

        public async Task<ApiResponse<List<DepartmentAttendanceSummary>>> GetDepartmentStatisticsAsync(DateTime fromDate, DateTime toDate)
        {
            var overview = await GetOverviewAsync(new ReportFilterModel { FromDate = fromDate, ToDate = toDate, PeriodType = PeriodHelper.Custom });
            return ApiResponse<List<DepartmentAttendanceSummary>>.Ok(overview.Data?.Departments ?? new(), "Lấy thống kê theo phòng ban thành công.");
        }

        // =====================================================================
        // BÁO CÁO CÁ NHÂN
        // =====================================================================
        public async Task<ApiResponse<EmployeeAttendanceReportModel>> GetEmployeePersonalReportAsync(
            string userId, DateTime fromDate, DateTime toDate, string? periodLabel = null, string periodType = PeriodHelper.Custom)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return ApiResponse<EmployeeAttendanceReportModel>.Fail("Không tìm thấy nhân viên.");

            var from = fromDate.Date;
            var to = toDate.Date;
            if (to < from) (from, to) = (to, from);
            var period = new PeriodRange(from, to, periodLabel ?? $"{from:dd/MM/yyyy} - {to:dd/MM/yyyy}", periodType);

            var records = await _context.AttendanceRecords.AsNoTracking()
                .Where(a => a.UserId == userId && a.AttendanceDate >= from && a.AttendanceDate <= to)
                .OrderBy(a => a.AttendanceDate)
                .ToListAsync();

            var configs = await _context.SalaryConfigs.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
            var calc = SalaryCalculator.Calculate(user, SalaryCalculator.PickConfig(configs, to), records, period);
            var today = VnTime.Today;

            var daysPresent = records.Where(r => r.CheckInTime.HasValue).Select(r => r.AttendanceDate).Distinct().Count();

            var report = new EmployeeAttendanceReportModel
            {
                UserId = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                Department = user.Department,
                Position = user.Position,
                PeriodType = period.PeriodType,
                PeriodLabel = period.Label,
                FromDate = from,
                ToDate = to,
                TotalDaysPresent = daysPresent,
                TotalHoursWorked = calc.TotalWorkingHours,
                RegularHours = calc.RegularHours,
                OvertimeHours = calc.OvertimeHours,
                TotalManualEntries = records.Count(r => r.IsManualEntry),
                MissingCheckouts = records.Count(r => r.CheckOutTime == null && r.AttendanceDate < today),
                AverageHoursPerDay = calc.WorkingDaysCount > 0 ? Math.Round(calc.TotalWorkingHours / calc.WorkingDaysCount, 2) : 0,
                EstimatedSalary = calc.TotalSalary,
                HourlyRate = calc.HourlyRate,
                DailyRecords = records.Select(r => new EmployeeDailyAttendance
                {
                    Date = r.AttendanceDate,
                    CheckInTime = r.CheckInTime,
                    CheckOutTime = r.CheckOutTime,
                    HoursWorked = r.TotalWorkingHours,
                    AuthenticationMethod = r.AuthenticationMethod,
                    IsManual = r.IsManualEntry,
                    Remarks = r.Remarks
                }).ToList()
            };

            return ApiResponse<EmployeeAttendanceReportModel>.Ok(report, "Tạo báo cáo cá nhân thành công.");
        }
    }
}
