using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Helpers
{
    /// <summary>
    /// Bộ tính lương DUY NHẤT của hệ thống (Bảng lương, Báo cáo, Email, App đều dùng chung).
    ///
    /// Quy tắc:
    ///  1. Chỉ tính các ca đã hoàn tất (có cả giờ vào và giờ ra).
    ///  2. Gom giờ làm theo từng NGÀY.
    ///  3. Giờ trong ngày ≤ giờ chuẩn (mặc định 8h)  → toàn bộ là giờ chuẩn.
    ///     Giờ trong ngày  > giờ chuẩn              → phần vượt là giờ tăng ca (OT).
    ///  4. Lương chuẩn = Σ giờ chuẩn × đơn giá/giờ
    ///     Lương OT    = Σ giờ OT    × đơn giá/giờ × hệ số OT (mặc định 1.5)
    ///     Tổng lương  = Lương chuẩn + Lương OT (làm tròn đến đồng).
    /// </summary>
    public static class SalaryCalculator
    {
        public const decimal DefaultHourlyRate = 55000m;
        public const decimal DefaultOvertimeMultiplier = 1.5m;
        public const double DefaultStandardDailyHours = 8.0;

        /// <summary>
        /// Bảng định mức đơn giá lương theo giờ chuẩn ứng với từng Chức vụ doanh nghiệp.
        /// </summary>
        public static decimal ResolveStandardHourlyRateByPosition(string? position)
        {
            if (string.IsNullOrWhiteSpace(position))
                return DefaultHourlyRate;

            var p = position.Trim().ToLowerInvariant();
            if (p.Contains("giám đốc") || p.Contains("điều hành"))
                return 150000m;
            if (p.Contains("trưởng phòng") || p.Contains("quản lý") || p.Contains("manager"))
                return 110000m;
            if (p.Contains("trưởng nhóm") || p.Contains("team lead") || p.Contains("leader"))
                return 90000m;
            if (p.Contains("cao cấp") || p.Contains("senior") || p.Contains("kỹ sư") || p.Contains("chuyên viên chính"))
                return 75000m;
            if (p.Contains("thử việc") || p.Contains("probation"))
                return 42000m;
            if (p.Contains("thực tập") || p.Contains("intern"))
                return 35000m;

            return DefaultHourlyRate; // Nhân viên chính thức / Chuyên viên: 55.000 đ/giờ
        }

        public static SalaryConfig? PickConfig(IEnumerable<SalaryConfig> userConfigs, DateTime periodEnd)
        {
            var list = userConfigs.ToList();
            if (list.Count == 0) return null;

            var endOfPeriod = periodEnd.Date.AddDays(1);
            return list
                       .Where(c => c.EffectiveDate < endOfPeriod)
                       .OrderByDescending(c => c.EffectiveDate)
                       .ThenByDescending(c => c.CreatedAt)
                       .FirstOrDefault()
                   ?? list.OrderByDescending(c => c.EffectiveDate).ThenByDescending(c => c.CreatedAt).First();
        }

        public static SalaryCalculationResultModel Calculate(
            ApplicationUser user,
            SalaryConfig? config,
            IEnumerable<AttendanceRecord> records,
            PeriodRange period,
            decimal bonusSalary = 0,
            string? bonusReason = null)
        {
            var hourlyRate = config?.HourlyRate ?? DefaultHourlyRate;
            var multiplier = config?.OvertimeRateMultiplier ?? DefaultOvertimeMultiplier;
            var standardHours = config?.StandardDailyHours ?? DefaultStandardDailyHours;

            var inRange = records
                .Where(r => r.UserId == user.Id && r.AttendanceDate >= period.From && r.AttendanceDate <= period.To)
                .ToList();

            var completed = inRange.Where(r => r.CheckInTime.HasValue && r.CheckOutTime.HasValue).ToList();
            var incomplete = inRange.Count - completed.Count;

            // Tính số ngày làm việc tiêu chuẩn trong kỳ (Thứ 2 đến Thứ 7, Chủ Nhật nghỉ tuần)
            var startCount = (user.HireDate.HasValue && user.HireDate.Value.Date > period.From.Date)
                ? user.HireDate.Value.Date
                : period.From.Date;
            int standardWorkingDays = 0;
            for (var d = startCount; d <= period.To.Date; d = d.AddDays(1))
            {
                if (d.DayOfWeek != DayOfWeek.Sunday)
                {
                    standardWorkingDays++;
                }
            }

            double totalHours = 0, regularHours = 0, overtimeHours = 0;
            var daily = new List<SalaryDailyItem>();

            foreach (var day in completed.GroupBy(r => r.AttendanceDate.Date).OrderBy(g => g.Key))
            {
                var isSunday = day.Key.DayOfWeek == DayOfWeek.Sunday;
                var hours = Math.Round(day.Sum(r => r.TotalWorkingHours), 2);

                if (isSunday)
                {
                    // Chủ Nhật là ngày nghỉ tuần. Nếu có đi làm, theo Điều 98 BLLĐ tính làm thêm ngày nghỉ với hệ số x2.0
                    var sundayMultiplier = Math.Max(multiplier, 2.0m);
                    totalHours += hours;
                    overtimeHours += hours;
                    var amount = (decimal)hours * hourlyRate * sundayMultiplier;

                    daily.Add(new SalaryDailyItem
                    {
                        Date = day.Key,
                        CheckInTime = day.Min(r => r.CheckInTime),
                        CheckOutTime = day.Max(r => r.CheckOutTime),
                        Hours = hours,
                        RegularHours = 0,
                        OvertimeHours = hours,
                        Amount = Math.Round(amount, 0),
                        IsManual = day.Any(r => r.IsManualEntry),
                        IsWeekend = true,
                        DayMultiplier = sundayMultiplier
                    });
                }
                else
                {
                    // Thứ 2 đến Thứ 7: Ngày làm việc tiêu chuẩn
                    var reg = Math.Min(hours, standardHours);
                    var ot = Math.Max(0, Math.Round(hours - standardHours, 2));

                    totalHours += hours;
                    regularHours += reg;
                    overtimeHours += ot;

                    var amount = (decimal)reg * hourlyRate + (decimal)ot * hourlyRate * multiplier;

                    daily.Add(new SalaryDailyItem
                    {
                        Date = day.Key,
                        CheckInTime = day.Min(r => r.CheckInTime),
                        CheckOutTime = day.Max(r => r.CheckOutTime),
                        Hours = hours,
                        RegularHours = Math.Round(reg, 2),
                        OvertimeHours = ot,
                        Amount = Math.Round(amount, 0),
                        IsManual = day.Any(r => r.IsManualEntry),
                        IsWeekend = false,
                        DayMultiplier = 1.0m
                    });
                }
            }

            totalHours = Math.Round(totalHours, 2);
            regularHours = Math.Round(regularHours, 2);
            overtimeHours = Math.Round(overtimeHours, 2);

            var regularSalary = Math.Round((decimal)regularHours * hourlyRate, 0);
            var totalAmount = daily.Sum(d => d.Amount);
            var overtimeSalary = Math.Max(0, totalAmount - regularSalary);

            var hireDate = user.HireDate?.Date;
            string? hireNote = null;
            if (hireDate.HasValue)
            {
                if (hireDate.Value > period.To)
                {
                    hireNote = $"Nhân viên gia nhập sau kỳ công này ({hireDate.Value:dd/MM/yyyy}). Chưa phát sinh ngày công.";
                }
                else if (hireDate.Value > period.From)
                {
                    hireNote = $"Gia nhập công ty giữa kỳ vào ngày {hireDate.Value:dd/MM/yyyy}. Bảng lương tính theo đúng số công & giờ làm thực tế kể từ ngày vào làm.";
                }
            }

            return new SalaryCalculationResultModel
            {
                UserId = user.Id,
                FullName = user.FullName,
                Username = user.UserName,
                Email = user.Email ?? string.Empty,
                Department = user.Department,
                Position = user.Position,
                PeriodType = period.PeriodType,
                PeriodDescription = period.Label,
                FromDate = period.From,
                ToDate = period.To,
                StandardWorkingDays = standardWorkingDays,
                WorkingDaysCount = daily.Count,
                TotalWorkingHours = totalHours,
                RegularHours = regularHours,
                OvertimeHours = overtimeHours,
                StandardDailyHours = standardHours,
                HourlyRate = hourlyRate,
                OvertimeMultiplier = multiplier,
                RegularSalary = regularSalary,
                OvertimeSalary = overtimeSalary,
                BonusSalary = bonusSalary,
                BonusReason = bonusReason,
                TotalSalary = regularSalary + overtimeSalary + bonusSalary,
                IncompleteRecords = incomplete,
                HireDate = hireDate,
                HireNote = hireNote,
                DailyBreakdown = daily,
                CalculatedAt = VnTime.Now
            };
        }

        /// <summary>
        /// Tính số giờ làm việc thực tế của một ca làm việc (đã trừ 1h nghỉ trưa 12:00 - 13:00 đối với Ca Hành Chính).
        /// QUY TẮC CHUẨN DOANH NGHIỆP:
        /// 1. Vào làm sớm trước giờ bắt đầu ca (ví dụ đến lúc 07:24 khi ca 08:00): Giờ tính công BẮT ĐẦU từ đúng giờ ca (08:00). Tuyệt đối không tính OT vào sớm!
        /// 2. Tan ca trong phạm vi ca tiêu chuẩn (08:00 - 17:00): Giờ tính công chốt trần tại giờ kết thúc ca (17:00), không tính OT cho việc nán lại bấm trễ trong phạm vi cổng tan ca.
        /// </summary>
        public static double CalculateWorkingHours(DateTime checkIn, DateTime checkOut, string? shiftName = null)
        {
            if (checkOut <= checkIn) return 0;

            var normalizedShift = WorkShifts.NormalizeShiftName(shiftName);
            var (shiftStart, shiftEnd, _, _, _) = WorkShifts.GetSchedule(normalizedShift);

            // 1. Chuẩn hóa giờ vào: Nếu đến sớm trước giờ ca -> tính từ giờ bắt đầu ca
            var shiftStartDate = checkIn.Date.Add(shiftStart);
            var effectiveIn = checkIn < shiftStartDate ? shiftStartDate : checkIn;

            // 2. Chuẩn hóa giờ ra: Đối với ca tiêu chuẩn, chốt trần tại giờ kết thúc ca
            var shiftEndDate = checkIn.Date.Add(shiftEnd);
            var effectiveOut = checkOut;
            if (checkOut > shiftEndDate)
            {
                effectiveOut = shiftEndDate;
            }

            if (effectiveOut <= effectiveIn) return 0;

            // 3. Ca Hành Chính (08:00 - 17:00): Trừ thời gian nghỉ trưa 12:00 - 13:00 (1 tiếng)
            if (normalizedShift == WorkShifts.HanhChinh)
            {
                var lunchStart = checkIn.Date.AddHours(12); // 12:00
                var lunchEnd = checkIn.Date.AddHours(13);   // 13:00

                var totalDuration = (effectiveOut - effectiveIn).TotalHours;

                // Tính thời gian giao thoa giữa thời gian làm việc thực tế và khung nghỉ trưa 12h-13h
                var overlapStart = effectiveIn > lunchStart ? effectiveIn : lunchStart;
                var overlapEnd = effectiveOut < lunchEnd ? effectiveOut : lunchEnd;
                var lunchBreakDeduction = 0.0;
                if (overlapEnd > overlapStart)
                {
                    lunchBreakDeduction = (overlapEnd - overlapStart).TotalHours;
                }

                var actualHours = Math.Max(0, totalDuration - lunchBreakDeduction);
                return Math.Round(actualHours, 2);
            }

            // 4. Các ca khác (Ca Sáng 06:00 - 14:00, Ca Chiều 14:00 - 22:00, Ca Tối 18:00 - 22:00):
            return Math.Max(0, Math.Round((effectiveOut - effectiveIn).TotalHours, 2));
        }

        /// <summary>Số giờ làm của một ca (làm tròn 2 chữ số thập phân, đã tính nghỉ trưa nếu có).</summary>
        public static double ShiftHours(DateTime checkIn, DateTime checkOut, string? shiftName = null)
            => CalculateWorkingHours(checkIn, checkOut, shiftName);
    }
}
