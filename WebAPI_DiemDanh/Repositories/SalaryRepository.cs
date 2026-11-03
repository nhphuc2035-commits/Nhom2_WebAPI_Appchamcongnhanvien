using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebAPI_DiemDanh.Data;
using WebAPI_DiemDanh.Helpers;
using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Repositories
{
    public class SalaryRepository : ISalaryRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public SalaryRepository(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<ApiResponse<SalaryCalculationResultModel>> CalculateEmployeeSalaryAsync(
            string userId, DateTime fromDate, DateTime toDate, string? periodDescription = null, string periodType = PeriodHelper.Custom)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return ApiResponse<SalaryCalculationResultModel>.Fail("Không tìm thấy nhân viên.");

            var from = fromDate.Date;
            var to = toDate.Date;
            if (to < from) (from, to) = (to, from);

            var period = new PeriodRange(from, to, periodDescription ?? $"{from:dd/MM/yyyy} - {to:dd/MM/yyyy}", periodType);

            var configs = await _context.SalaryConfigs.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
            var records = await _context.AttendanceRecords.AsNoTracking()
                .Where(a => a.UserId == userId && a.AttendanceDate >= from && a.AttendanceDate <= to)
                .ToListAsync();

            var bonus = await _context.SalaryBonuses.AsNoTracking()
                .Where(b => b.UserId == userId &&
                           ((b.Year == from.Year && b.Month == from.Month) || (b.Year == to.Year && b.Month == to.Month)))
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();

            var result = SalaryCalculator.Calculate(user, SalaryCalculator.PickConfig(configs, to), records, period, bonus?.Amount ?? 0m, bonus?.Reason);
            return ApiResponse<SalaryCalculationResultModel>.Ok(result, "Tính lương nhân viên thành công.");
        }

        public async Task<ApiResponse<SalaryPayrollSummaryModel>> CalculateAllSalariesAsync(CalculateSalaryFilterModel filter)
        {
            var type = PeriodHelper.Normalize(filter.PeriodType);
            if (type != PeriodHelper.Week && type != PeriodHelper.Custom) type = PeriodHelper.Month;

            var period = PeriodHelper.Resolve(type, filter.Year, filter.Month, null, filter.WeekNumber, filter.FromDate, filter.ToDate);

            var adminIds = (await _userManager.GetUsersInRoleAsync(UserRoles.Admin)).Select(u => u.Id).ToHashSet();

            var query = _userManager.Users.AsNoTracking().Where(u => u.IsActive);
            if (!string.IsNullOrWhiteSpace(filter.Department))
                query = query.Where(u => u.Department == filter.Department);
            if (!string.IsNullOrWhiteSpace(filter.UserId))
                query = query.Where(u => u.Id == filter.UserId);

            var users = (await query.OrderBy(u => u.FullName).ToListAsync())
                .Where(u => (!adminIds.Contains(u.Id) || u.Id == filter.UserId) &&
                            (!u.HireDate.HasValue || u.HireDate.Value.Date <= period.To))
                .ToList();

            var ids = users.Select(u => u.Id).ToList();
            var records = await _context.AttendanceRecords.AsNoTracking()
                .Where(a => ids.Contains(a.UserId) && a.AttendanceDate >= period.From && a.AttendanceDate <= period.To)
                .ToListAsync();
            var configs = await _context.SalaryConfigs.AsNoTracking().Where(s => ids.Contains(s.UserId)).ToListAsync();

            var recordsByUser = records.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.ToList());
            var configsByUser = configs.GroupBy(c => c.UserId).ToDictionary(g => g.Key, g => g.ToList());

            var bonuses = await _context.SalaryBonuses.AsNoTracking()
                .Where(b => ids.Contains(b.UserId) &&
                           ((b.Year == period.From.Year && b.Month == period.From.Month) ||
                            (b.Year == period.To.Year && b.Month == period.To.Month)))
                .ToListAsync();
            var bonusesByUser = bonuses.GroupBy(b => b.UserId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());

            var items = users.Select(u =>
            {
                var bonusObj = bonusesByUser.TryGetValue(u.Id, out var b) ? b : null;
                return SalaryCalculator.Calculate(
                    u,
                    configsByUser.TryGetValue(u.Id, out var cs) ? SalaryCalculator.PickConfig(cs, period.To) : null,
                    recordsByUser.TryGetValue(u.Id, out var rs) ? rs : new List<AttendanceRecord>(),
                    period,
                    bonusObj?.Amount ?? 0m,
                    bonusObj?.Reason);
            }).ToList();

            var summary = new SalaryPayrollSummaryModel
            {
                PeriodType = period.PeriodType,
                PeriodDescription = period.Label,
                FromDate = period.From,
                ToDate = period.To,
                EmployeeCount = items.Count,
                TotalHours = Math.Round(items.Sum(i => i.TotalWorkingHours), 2),
                TotalRegularHours = Math.Round(items.Sum(i => i.RegularHours), 2),
                TotalOvertimeHours = Math.Round(items.Sum(i => i.OvertimeHours), 2),
                TotalRegularSalary = items.Sum(i => i.RegularSalary),
                TotalOvertimeSalary = items.Sum(i => i.OvertimeSalary),
                TotalBonusSalary = items.Sum(i => i.BonusSalary),
                TotalPayroll = items.Sum(i => i.TotalSalary),
                Items = items
            };

            return ApiResponse<SalaryPayrollSummaryModel>.Ok(summary, $"Đã tính bảng lương {period.Label} cho {items.Count} nhân viên.");
        }

        public async Task<ApiResponse<SalaryConfig>> SetSalaryConfigAsync(SalaryConfigModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
                return ApiResponse<SalaryConfig>.Fail("Không tìm thấy nhân viên.");

            var now = VnTime.Now;
            var config = new SalaryConfig
            {
                UserId = model.UserId,
                HourlyRate = model.HourlyRate,
                OvertimeRateMultiplier = model.OvertimeRateMultiplier,
                StandardDailyHours = model.StandardDailyHours,
                Note = string.IsNullOrWhiteSpace(model.Note) ? "Cập nhật cấu hình lương" : model.Note.Trim(),
                EffectiveDate = now,
                CreatedAt = now
            };

            _context.SalaryConfigs.Add(config);
            await _context.SaveChangesAsync();

            return ApiResponse<SalaryConfig>.Ok(config, $"Đã cập nhật cấu hình lương cho {user.FullName}.");
        }

        public async Task<ApiResponse<SalaryConfig>> GetSalaryConfigAsync(string userId)
        {
            var configs = await _context.SalaryConfigs.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
            var config = SalaryCalculator.PickConfig(configs, VnTime.Today) ?? new SalaryConfig
            {
                UserId = userId,
                HourlyRate = SalaryCalculator.DefaultHourlyRate,
                OvertimeRateMultiplier = SalaryCalculator.DefaultOvertimeMultiplier,
                StandardDailyHours = SalaryCalculator.DefaultStandardDailyHours,
                Note = "Mức lương mặc định của hệ thống"
            };

            return ApiResponse<SalaryConfig>.Ok(config, "Lấy thông tin cấu hình lương thành công.");
        }

        public async Task<ApiResponse<List<SalaryConfig>>> GetSalaryConfigHistoryAsync(string userId)
        {
            var list = await _context.SalaryConfigs.AsNoTracking()
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.EffectiveDate).ThenByDescending(s => s.CreatedAt)
                .ToListAsync();
            return ApiResponse<List<SalaryConfig>>.Ok(list, "Lấy lịch sử cấu hình lương thành công.");
        }

        public async Task<ApiResponse<List<PositionRate>>> GetPositionRatesAsync()
        {
            var list = await _context.PositionRates.AsNoTracking().OrderBy(p => p.HourlyRate).ToListAsync();
            if (list.Count == 0)
            {
                var defaults = new List<PositionRate>
                {
                    new() { PositionName = "Thực Tập Sinh (Intern)", HourlyRate = 35000, Role = "User", OvertimeMultiplier = 1.5m, StandardDailyHours = 8.0 },
                    new() { PositionName = "Nhân Viên Thử Việc", HourlyRate = 42000, Role = "User", OvertimeMultiplier = 1.5m, StandardDailyHours = 8.0 },
                    new() { PositionName = "Nhân Viên Chính Thức", HourlyRate = 55000, Role = "User", OvertimeMultiplier = 1.5m, StandardDailyHours = 8.0 },
                    new() { PositionName = "Chuyên Viên Cao Cấp (Senior)", HourlyRate = 75000, Role = "User", OvertimeMultiplier = 1.5m, StandardDailyHours = 8.0 },
                    new() { PositionName = "Trưởng Nhóm (Team Lead)", HourlyRate = 90000, Role = "User", OvertimeMultiplier = 1.5m, StandardDailyHours = 8.0 },
                    new() { PositionName = "Trưởng Phòng / Quản Lý", HourlyRate = 110000, Role = "Manager", OvertimeMultiplier = 1.5m, StandardDailyHours = 8.0 },
                    new() { PositionName = "Giám Đốc Điều Hành", HourlyRate = 150000, Role = "Admin", OvertimeMultiplier = 1.5m, StandardDailyHours = 8.0 },
                };
                _context.PositionRates.AddRange(defaults);
                await _context.SaveChangesAsync();
                list = defaults;
            }
            return ApiResponse<List<PositionRate>>.Ok(list, "Lấy danh sách định mức lương chức vụ thành công.");
        }

        public async Task<ApiResponse<PositionRate>> UpdatePositionRateAsync(UpdatePositionRateModel model)
        {
            var posName = model.PositionName.Trim();
            var existing = await _context.PositionRates.FirstOrDefaultAsync(p => p.PositionName.ToLower() == posName.ToLower());
            var now = VnTime.Now;

            if (existing == null)
            {
                existing = new PositionRate
                {
                    PositionName = posName,
                    HourlyRate = model.HourlyRate,
                    Role = model.Role ?? "User",
                    OvertimeMultiplier = model.OvertimeMultiplier ?? 1.5m,
                    StandardDailyHours = model.StandardDailyHours ?? 8.0,
                    UpdatedAt = now
                };
                _context.PositionRates.Add(existing);
            }
            else
            {
                existing.HourlyRate = model.HourlyRate;
                if (!string.IsNullOrWhiteSpace(model.Role)) existing.Role = model.Role;
                if (model.OvertimeMultiplier.HasValue) existing.OvertimeMultiplier = model.OvertimeMultiplier.Value;
                if (model.StandardDailyHours.HasValue) existing.StandardDailyHours = model.StandardDailyHours.Value;
                existing.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();

            int updatedEmployeeCount = 0;
            if (model.ApplyToCurrentEmployees)
            {
                var matchedUsers = await _userManager.Users
                    .Where(u => u.Position != null && (u.Position.ToLower() == posName.ToLower() || u.Position.ToLower().Contains(posName.ToLower())))
                    .ToListAsync();

                foreach (var emp in matchedUsers)
                {
                    var newCfg = new SalaryConfig
                    {
                        UserId = emp.Id,
                        HourlyRate = model.HourlyRate,
                        OvertimeRateMultiplier = model.OvertimeMultiplier ?? 1.5m,
                        StandardDailyHours = model.StandardDailyHours ?? 8.0,
                        Note = $"Cập nhật theo thang bảng lương chức vụ {posName}",
                        EffectiveDate = now,
                        CreatedAt = now
                    };
                    _context.SalaryConfigs.Add(newCfg);
                    updatedEmployeeCount++;
                }

                if (updatedEmployeeCount > 0)
                {
                    await _context.SaveChangesAsync();
                }
            }

            var msg = updatedEmployeeCount > 0
                ? $"Đã cập nhật định mức lương chức vụ '{posName}' thành {existing.HourlyRate:N0} đ/h và đồng bộ cho {updatedEmployeeCount} nhân viên."
                : $"Đã cập nhật định mức lương chức vụ '{posName}' thành {existing.HourlyRate:N0} đ/h.";

            return ApiResponse<PositionRate>.Ok(existing, msg);
        }

        public async Task<ApiResponse<SalaryBonus>> SetSalaryBonusAsync(SetSalaryBonusModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
                return ApiResponse<SalaryBonus>.Fail("Không tìm thấy nhân viên.");

            var existing = await _context.SalaryBonuses.FirstOrDefaultAsync(b => b.UserId == model.UserId && b.Year == model.Year && b.Month == model.Month);
            var now = VnTime.Now;

            if (existing == null)
            {
                existing = new SalaryBonus
                {
                    UserId = model.UserId,
                    Year = model.Year,
                    Month = model.Month,
                    Amount = model.Amount,
                    Reason = string.IsNullOrWhiteSpace(model.Reason) ? "Thưởng cống hiến đóng góp đặc biệt" : model.Reason.Trim(),
                    CreatedAt = now
                };
                _context.SalaryBonuses.Add(existing);
            }
            else
            {
                existing.Amount = model.Amount;
                existing.Reason = string.IsNullOrWhiteSpace(model.Reason) ? existing.Reason : model.Reason.Trim();
                existing.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();

            return ApiResponse<SalaryBonus>.Ok(existing, $"Đã cập nhật mức thưởng {existing.Amount:N0} đ cho {user.FullName} (Tháng {model.Month}/{model.Year}).");
        }

        public async Task<ApiResponse<List<SalaryBonus>>> GetSalaryBonusesAsync(int? year, int? month, string? userId)
        {
            var query = _context.SalaryBonuses.AsNoTracking().AsQueryable();
            if (year.HasValue) query = query.Where(b => b.Year == year.Value);
            if (month.HasValue) query = query.Where(b => b.Month == month.Value);
            if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(b => b.UserId == userId);

            var list = await query.OrderByDescending(b => b.CreatedAt).ToListAsync();
            return ApiResponse<List<SalaryBonus>>.Ok(list, "Lấy danh sách tiền thưởng thành công.");
        }

        public async Task<ApiResponse<bool>> DeleteSalaryBonusAsync(int id)
        {
            var bonus = await _context.SalaryBonuses.FindAsync(id);
            if (bonus == null)
                return ApiResponse<bool>.Fail("Không tìm thấy khoản tiền thưởng.");

            _context.SalaryBonuses.Remove(bonus);
            await _context.SaveChangesAsync();
            return ApiResponse<bool>.Ok(true, "Đã xóa khoản tiền thưởng.");
        }
    }
}
