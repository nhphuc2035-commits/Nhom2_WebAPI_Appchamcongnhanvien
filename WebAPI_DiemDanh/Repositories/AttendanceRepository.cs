using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebAPI_DiemDanh.Data;
using WebAPI_DiemDanh.Helpers;
using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Repositories
{
    public class AttendanceRepository : IAttendanceRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;

        public AttendanceRepository(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment env)
        {
            _context = context;
            _userManager = userManager;
            _env = env;
        }

        // ------------------------------------------------------------------
        // Xác minh vân tay chính chủ: token sinh trắc học + thiết bị đã đăng ký
        // ------------------------------------------------------------------
        private static string? VerifyBiometric(ApplicationUser user, string? biometricToken, string? deviceId)
        {
            if (string.IsNullOrEmpty(user.BiometricKey))
            {
                return "Bạn chưa đăng ký vân tay với hệ thống. Vui lòng vào mục Tài khoản → Đăng ký vân tay trước khi điểm danh.";
            }

            if (string.IsNullOrEmpty(biometricToken) || biometricToken != user.BiometricKey)
            {
                return "Xác thực vân tay không khớp với hồ sơ đã đăng ký. Không thể điểm danh hộ. Nếu bạn vừa đổi vân tay trong cài đặt máy, hãy liên hệ quản lý để đăng ký lại.";
            }

            if (!string.IsNullOrEmpty(user.BiometricDeviceId) && user.BiometricDeviceId != deviceId)
            {
                return "Bạn đang điểm danh trên thiết bị khác với thiết bị đã đăng ký. Mỗi nhân viên chỉ được điểm danh trên điện thoại chính chủ.";
            }

            return null;
        }

        public async Task<ApiResponse<AttendanceRecordResponseModel>> CheckInAsync(string userId, CheckInRequestModel model)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Không tìm thấy nhân viên trong hệ thống.");

            if (!user.IsActive)
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Tài khoản của bạn đang bị khóa. Vui lòng liên hệ quản trị viên.");

            if (await _userManager.IsInRoleAsync(user, UserRoles.Admin))
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Tài khoản quản trị không dùng để điểm danh.");

            var bioError = VerifyBiometric(user, model.BiometricToken, model.DeviceId);
            if (bioError != null)
                return ApiResponse<AttendanceRecordResponseModel>.Fail(bioError);

            var now = VnTime.Now;
            var today = now.Date;

            var assignedShift = WorkShifts.NormalizeAssignedShift(user.AssignedShift);
            var selectedShift = string.IsNullOrWhiteSpace(model.ShiftName)
                ? (assignedShift == WorkShifts.XoayCa ? WorkShifts.HanhChinh : assignedShift)
                : WorkShifts.NormalizeShiftName(model.ShiftName);

            // 1. Ràng buộc phân công ca của Admin (chỉ cho phép chấm đúng ca được phân công hoặc Ca Tối tăng cường, trừ khi được phân công Xoay ca linh hoạt)
            if (assignedShift != WorkShifts.XoayCa && selectedShift != assignedShift && selectedShift != WorkShifts.CaToi)
            {
                return ApiResponse<AttendanceRecordResponseModel>.Fail(
                    $"Bạn được phân công cố định [{assignedShift}], không được phép chấm công vào [{selectedShift}]. Vui lòng chọn đúng ca được phân công hoặc liên hệ Admin đổi ca.");
            }

            // 2. Kiểm tra các bản ghi chấm công trong ngày hôm nay
            var todayRecords = await _context.AttendanceRecords
                .Where(a => a.UserId == userId && a.AttendanceDate == today)
                .OrderByDescending(a => a.Id)
                .ToListAsync();

            var openRecord = todayRecords.FirstOrDefault(a => a.CheckOutTime == null);
            if (openRecord != null)
            {
                return ApiResponse<AttendanceRecordResponseModel>.Fail(
                    $"Bạn đã vào [{openRecord.ShiftName ?? WorkShifts.HanhChinh}] lúc {openRecord.CheckInTime:HH:mm} hôm nay và chưa tan ca.");
            }

            if (todayRecords.Count > 0)
            {
                if (selectedShift != WorkShifts.CaToi)
                {
                    return ApiResponse<AttendanceRecordResponseModel>.Fail(
                        "Bạn đã hoàn thành ca làm việc chính hôm nay. Nếu làm thêm ca tối, vui lòng chọn 'Ca Tối (18:00 - 22:00)'.");
                }

                if (todayRecords.Any(a => WorkShifts.NormalizeShiftName(a.ShiftName) == WorkShifts.CaToi))
                {
                    return ApiResponse<AttendanceRecordResponseModel>.Fail(
                        "Bạn đã hoàn thành Ca Tối (18:00 - 22:00) hôm nay. Hẹn gặp lại vào ngày mai!");
                }
            }

            // 3. Ràng buộc khung giờ mở cổng chấm công theo từng ca để chống chấm công bừa bãi
            var (start, end, earliest, latest, _) = WorkShifts.GetSchedule(selectedShift);
            var timeOfDay = now.TimeOfDay;

            if (timeOfDay < earliest)
            {
                return ApiResponse<AttendanceRecordResponseModel>.Fail(
                    $"Chưa đến giờ mở cổng chấm công cho [{selectedShift}]. Cổng mở từ {earliest:hh\\:mm} đến {latest:hh\\:mm} (giờ hiện tại: {now:HH:mm}).");
            }

            if (timeOfDay > latest)
            {
                return ApiResponse<AttendanceRecordResponseModel>.Fail(
                    $"Đã qua khung giờ cho phép vào [{selectedShift}] (cổng đóng lúc {latest:hh\\:mm}, ca kết thúc lúc {end:hh\\:mm}). Vui lòng chọn ca phù hợp hoặc báo Quản lý chấm công thủ công.");
            }

            // 4. Tự động đối chiếu giờ chuẩn của ca để ghi nhận Đúng giờ / Đi trễ (ân hạn 15 phút)
            string shiftStatusNote;
            if (selectedShift == WorkShifts.CaToi)
            {
                // Ca Tối là tăng ca tự nguyện: tự do vào ca trong khung giờ mở cổng (17:30 - 21:30), làm bao nhiêu tính bấy nhiêu
                shiftStatusNote = "[Vào ca tăng ca tự nguyện]";
            }
            else
            {
                var graceTime = start.Add(TimeSpan.FromMinutes(15));
                if (timeOfDay <= graceTime)
                {
                    shiftStatusNote = "[Vào ca đúng giờ]";
                }
                else
                {
                    var lateMinutes = (int)Math.Ceiling((timeOfDay - start).TotalMinutes);
                    shiftStatusNote = $"[Đi trễ {lateMinutes} phút]";
                }
            }

            var userRemark = string.IsNullOrWhiteSpace(model.Remarks) ? null : model.Remarks.Trim();
            var combinedRemark = userRemark != null ? $"{shiftStatusNote} {userRemark}" : shiftStatusNote;

            // Xử lý lưu ảnh Selfie xác thực khoảnh khắc check-in (nếu có)
            string? savedProofPath = null;
            if (!string.IsNullOrWhiteSpace(model.ProofImageBase64))
            {
                try
                {
                    var base64Data = model.ProofImageBase64.Trim();
                    if (base64Data.Contains(","))
                    {
                        base64Data = base64Data.Substring(base64Data.IndexOf(",") + 1);
                    }
                    var imageBytes = Convert.FromBase64String(base64Data);
                    var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var uploadsDir = Path.Combine(webRoot, "uploads", "proofs");
                    if (!Directory.Exists(uploadsDir))
                    {
                        Directory.CreateDirectory(uploadsDir);
                    }
                    var safeUser = userId.Replace("-", "");
                    var fileName = $"selfie_{safeUser}_{now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.jpg";
                    var filePath = Path.Combine(uploadsDir, fileName);
                    await File.WriteAllBytesAsync(filePath, imageBytes);
                    savedProofPath = $"/uploads/proofs/{fileName}";
                }
                catch
                {
                    // Tránh gián đoạn chấm công nếu ghi file thất bại
                }
            }

            var record = new AttendanceRecord
            {
                UserId = userId,
                AttendanceDate = today,
                ShiftName = selectedShift,
                CheckInTime = now,
                TotalWorkingHours = 0,
                AuthenticationMethod = AuthMethods.Fingerprint,
                IsManualEntry = false,
                Remarks = combinedRemark.Length > 255 ? combinedRemark[..255] : combinedRemark,
                ProofImagePath = savedProofPath,
                CreatedAt = now
            };

            _context.AttendanceRecords.Add(record);
            await _context.SaveChangesAsync();

            return ApiResponse<AttendanceRecordResponseModel>.Ok(Map(record, user, null), $"Vào [{selectedShift}] thành công lúc {now:HH:mm:ss} {shiftStatusNote}.");
        }

        public async Task<ApiResponse<AttendanceRecordResponseModel>> CheckOutAsync(string userId, CheckOutRequestModel model)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Không tìm thấy nhân viên.");

            if (!user.IsActive)
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Tài khoản của bạn đang bị khóa. Vui lòng liên hệ quản trị viên.");

            var bioError = VerifyBiometric(user, model.BiometricToken, model.DeviceId);
            if (bioError != null)
                return ApiResponse<AttendanceRecordResponseModel>.Fail(bioError);

            var now = VnTime.Now;
            var today = now.Date;

            var todayRecord = await _context.AttendanceRecords
                .Where(a => a.UserId == userId && a.AttendanceDate == today)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            if (todayRecord == null)
            {
                var stale = await _context.AttendanceRecords
                    .Where(a => a.UserId == userId && a.CheckOutTime == null && a.AttendanceDate < today)
                    .OrderByDescending(a => a.AttendanceDate)
                    .FirstOrDefaultAsync();

                return stale != null
                    ? ApiResponse<AttendanceRecordResponseModel>.Fail($"Bạn chưa vào ca hôm nay. Lưu ý: ca ngày {stale.AttendanceDate:dd/MM/yyyy} chưa được tan ca — vui lòng báo quản lý chấm công bù.")
                    : ApiResponse<AttendanceRecordResponseModel>.Fail("Bạn chưa vào ca hôm nay nên chưa thể tan ca.");
            }

            if (todayRecord.CheckOutTime != null)
                return ApiResponse<AttendanceRecordResponseModel>.Fail($"Bạn đã tan ca lúc {todayRecord.CheckOutTime:HH:mm} hôm nay.");

            if (todayRecord.CheckInTime.HasValue && (now - todayRecord.CheckInTime.Value).TotalSeconds < 60)
            {
                return ApiResponse<AttendanceRecordResponseModel>.Fail(
                    $"Bạn vừa vào ca lúc {todayRecord.CheckInTime:HH:mm:ss}. Vui lòng làm việc tối thiểu 1 phút trước khi bấm Tan ca để tránh thao tác nhầm.");
            }

            var shiftName = todayRecord.ShiftName ?? WorkShifts.HanhChinh;
            var (_, shiftEnd, _, _, _) = WorkShifts.GetSchedule(shiftName);
            var latestCheckOut = WorkShifts.GetLatestCheckOut(shiftName);

            // Kiểm tra đóng cổng tan ca khi ngoài giờ / lố giờ quá quy định
            if (now.TimeOfDay > latestCheckOut)
            {
                return ApiResponse<AttendanceRecordResponseModel>.Fail(
                    $"Đã qua khung giờ cho phép tan ca của [{shiftName}] (cổng tan ca đóng lúc {latestCheckOut:hh\\:mm}, ca kết thúc lúc {shiftEnd:hh\\:mm}). Ca làm việc đã bị khóa tự động để tránh trục lợi giờ công. Vui lòng báo Quản lý xác nhận giờ làm thực tế để chấm công bổ sung.");
            }

            todayRecord.CheckOutTime = now;

            // Chốt trần giờ tan ca tại mốc kết thúc ca (shiftEnd) đối với các ca tiêu chuẩn (Hành chính, Sáng, Chiều).
            // Cổng tan ca mở thêm 60 phút (hoặc 30 phút với ca chiều/tối) để nhân viên linh hoạt chấm công ra về,
            // nhưng giờ làm việc được chốt trần đúng mốc kết thúc ca để đảm bảo tính đúng 8 tiếng lương chuẩn,
            // tuyệt đối ngăn chặn việc nán lại hoặc chạy vào chấm trễ để trục lợi tiền tăng ca (OT).
            var effectiveCheckOut = now;
            var isCaToi = WorkShifts.NormalizeShiftName(shiftName) == WorkShifts.CaToi;
            if (now.TimeOfDay > shiftEnd)
            {
                effectiveCheckOut = todayRecord.AttendanceDate.Date.Add(shiftEnd);
            }

            todayRecord.TotalWorkingHours = todayRecord.CheckInTime.HasValue
                ? SalaryCalculator.ShiftHours(todayRecord.CheckInTime.Value, effectiveCheckOut, todayRecord.ShiftName)
                : 0;

            string outStatusNote;
            if (isCaToi)
            {
                // Ca tăng ca tự nguyện: tan ca tự do trong phạm vi ca, làm mấy giờ tính bấy nhiêu (tối đa 4.0h)
                outStatusNote = "[Hoàn tất tăng ca]";
            }
            else if (now.TimeOfDay < shiftEnd.Subtract(TimeSpan.FromMinutes(5)))
            {
                var earlyMins = (int)Math.Ceiling((shiftEnd - now.TimeOfDay).TotalMinutes);
                outStatusNote = $"[Về sớm {earlyMins} phút]";
            }
            else
            {
                outStatusNote = "[Tan ca đúng giờ]";
            }

            var extraRemark = !string.IsNullOrWhiteSpace(model.Remarks)
                ? $"{outStatusNote} {model.Remarks.Trim()}"
                : outStatusNote;

            var mergedRemarks = string.IsNullOrWhiteSpace(todayRecord.Remarks)
                ? extraRemark
                : $"{todayRecord.Remarks} | {extraRemark}";
            todayRecord.Remarks = mergedRemarks.Length > 255 ? mergedRemarks[..255] : mergedRemarks;

            todayRecord.UpdatedAt = now;
            await _context.SaveChangesAsync();

            return ApiResponse<AttendanceRecordResponseModel>.Ok(Map(todayRecord, user, null),
                $"Tan ca thành công lúc {now:HH:mm:ss} {outStatusNote}. Hôm nay bạn đã làm {todayRecord.TotalWorkingHours:0.##} giờ.");
        }

        public async Task<ApiResponse<TodayAttendanceStatusModel>> GetTodayStatusAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return ApiResponse<TodayAttendanceStatusModel>.Fail("Không tìm thấy nhân viên.");

            var now = VnTime.Now;
            var today = now.Date;

            var todayRecord = await _context.AttendanceRecords.AsNoTracking()
                .Where(a => a.UserId == userId && a.AttendanceDate == today)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            var stale = await _context.AttendanceRecords.AsNoTracking()
                .Where(a => a.UserId == userId && a.CheckOutTime == null && a.AttendanceDate < today)
                .OrderByDescending(a => a.AttendanceDate)
                .FirstOrDefaultAsync();

            var configs = await _context.SalaryConfigs.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
            var config = SalaryCalculator.PickConfig(configs, today);

            var status = todayRecord == null
                ? AttendanceStatuses.NotCheckedIn
                : ComputeStatus(todayRecord, today);

            var isShiftGateClosed = false;
            if (todayRecord != null)
            {
                var shift = todayRecord.ShiftName ?? WorkShifts.HanhChinh;
                var latestOut = WorkShifts.GetLatestCheckOut(shift);
                isShiftGateClosed = now.TimeOfDay > latestOut;
            }

            var canCheckInEveningOt = todayRecord != null
                && todayRecord.CheckOutTime != null
                && WorkShifts.NormalizeShiftName(todayRecord.ShiftName) != WorkShifts.CaToi
                && now.TimeOfDay >= new TimeSpan(17, 30, 0);

            var dto = new TodayAttendanceStatusModel
            {
                ServerTime = now,
                Today = today,
                Status = status,
                CanCheckIn = (todayRecord == null || canCheckInEveningOt) && user.IsActive,
                CanCheckOut = todayRecord != null && todayRecord.CheckOutTime == null && user.IsActive && !isShiftGateClosed,
                IsBiometricEnrolled = !string.IsNullOrEmpty(user.BiometricKey),
                StandardDailyHours = config?.StandardDailyHours ?? SalaryCalculator.DefaultStandardDailyHours,
                AssignedShift = WorkShifts.NormalizeAssignedShift(user.AssignedShift),
                Record = todayRecord != null ? Map(todayRecord, user, null) : null,
                PendingMissingCheckout = stale != null ? Map(stale, user, null) : null
            };

            return ApiResponse<TodayAttendanceStatusModel>.Ok(dto, "Lấy trạng thái ca làm hôm nay thành công.");
        }

        public async Task<ApiResponse<AttendanceRecordResponseModel>> ManualAttendanceAsync(ManualAttendanceModel model, string managerUserId)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Không tìm thấy nhân viên.");

            var manager = await _userManager.FindByIdAsync(managerUserId);
            var managerName = manager?.FullName ?? "Quản lý";

            var isManagerAdmin = manager != null && await _userManager.IsInRoleAsync(manager, UserRoles.Admin);
            var isTargetAdmin = await _userManager.IsInRoleAsync(user, UserRoles.Admin);

            if (isTargetAdmin)
            {
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Không được phép chấm công thủ công cho tài khoản Quản trị viên (Admin).");
            }

            if (!isManagerAdmin && model.UserId == managerUserId)
            {
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Để đảm bảo tính minh bạch kiểm soát nội bộ, Quản lý không được tự duyệt chấm công bù cho chính mình. Vui lòng liên hệ Admin để được chấm bù.");
            }

            var attendanceDate = model.AttendanceDate.Date;
            var now = VnTime.Now;

            if (attendanceDate > now.Date)
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Không thể chấm công cho ngày trong tương lai.");

            // Ghép ngày chấm công + giờ vào để tránh lệch ngày do client gửi
            var checkIn = attendanceDate.Add(model.CheckInTime.TimeOfDay);
            DateTime? checkOut = null;

            if (model.CheckOutTime.HasValue)
            {
                checkOut = model.CheckOutTime.Value;
                // Nếu client chỉ gửi giờ (ngày khác ngày chấm công), quy về ngày chấm công; nếu giờ ra < giờ vào → ca qua đêm
                if (checkOut.Value.Date != attendanceDate && checkOut.Value.Date != attendanceDate.AddDays(1))
                    checkOut = attendanceDate.Add(checkOut.Value.TimeOfDay);
                if (checkOut <= checkIn)
                    checkOut = checkOut.Value.Date == attendanceDate ? checkOut.Value.AddDays(1) : checkOut;

                if (checkOut <= checkIn)
                    return ApiResponse<AttendanceRecordResponseModel>.Fail("Giờ ra phải lớn hơn giờ vào.");
                if ((checkOut.Value - checkIn).TotalHours > 24)
                    return ApiResponse<AttendanceRecordResponseModel>.Fail("Một ca làm việc không được vượt quá 24 giờ.");
                if (checkOut > now)
                    return ApiResponse<AttendanceRecordResponseModel>.Fail("Giờ ra không được lớn hơn thời điểm hiện tại.");
            }

            if (checkIn > now)
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Giờ vào không được lớn hơn thời điểm hiện tại.");

            var resolvedShift = string.IsNullOrWhiteSpace(model.ShiftName)
                ? WorkShifts.NormalizeShiftName(user.AssignedShift)
                : WorkShifts.NormalizeShiftName(model.ShiftName);
            var hours = checkOut.HasValue ? SalaryCalculator.ShiftHours(checkIn, checkOut.Value, resolvedShift) : 0;
            var remark = $"[Chấm công thủ công bởi {managerName}]: {model.Reason.Trim()}";

            var existing = await _context.AttendanceRecords
                .Where(a => a.UserId == model.UserId && a.AttendanceDate == attendanceDate)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                if (!model.OverrideExisting)
                    return ApiResponse<AttendanceRecordResponseModel>.Fail("Ngày này đã có bản ghi chấm công. Bật tùy chọn ghi đè nếu muốn cập nhật.");

                existing.ShiftName = resolvedShift;
                existing.CheckInTime = checkIn;
                existing.CheckOutTime = checkOut;
                existing.TotalWorkingHours = hours;
                // Nếu bản ghi gốc đã có ảnh Selfie lúc vào ca -> đánh dấu là Hybrid (Vào tự chấm qua App, Ra quản lý chấm bù)
                existing.AuthenticationMethod = !string.IsNullOrEmpty(existing.ProofImagePath) ? "Hybrid" : AuthMethods.Manual;
                existing.IsManualEntry = true;
                existing.ManualEntryByUserId = managerUserId;
                existing.Remarks = remark.Length > 255 ? remark[..255] : remark;
                existing.UpdatedAt = now;

                await _context.SaveChangesAsync();
                return ApiResponse<AttendanceRecordResponseModel>.Ok(Map(existing, user, managerName), "Đã cập nhật chấm công thủ công.");
            }

            var record = new AttendanceRecord
            {
                UserId = model.UserId,
                AttendanceDate = attendanceDate,
                ShiftName = resolvedShift,
                CheckInTime = checkIn,
                CheckOutTime = checkOut,
                TotalWorkingHours = hours,
                AuthenticationMethod = AuthMethods.Manual,
                IsManualEntry = true,
                ManualEntryByUserId = managerUserId,
                Remarks = remark.Length > 255 ? remark[..255] : remark,
                CreatedAt = now
            };

            _context.AttendanceRecords.Add(record);
            await _context.SaveChangesAsync();

            return ApiResponse<AttendanceRecordResponseModel>.Ok(Map(record, user, managerName), "Đã thêm bản ghi chấm công thủ công.");
        }

        public async Task<ApiResponse<List<AttendanceRecordResponseModel>>> GetUserHistoryAsync(string userId, DateTime? fromDate, DateTime? toDate)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return ApiResponse<List<AttendanceRecordResponseModel>>.Fail("Không tìm thấy nhân viên.");

            var query = _context.AttendanceRecords.AsNoTracking().Where(a => a.UserId == userId);

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                query = query.Where(a => a.AttendanceDate >= from);
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date;
                query = query.Where(a => a.AttendanceDate <= to);
            }

            var rows = await query
                .OrderByDescending(a => a.AttendanceDate)
                .ThenByDescending(a => a.CheckInTime)
                .ToListAsync();

            var managerNames = await LoadManagerNamesAsync(rows);
            var list = rows.Select(a => Map(a, user, ManagerName(managerNames, a))).ToList();

            return ApiResponse<List<AttendanceRecordResponseModel>>.Ok(list, "Lấy lịch sử chấm công thành công.");
        }

        public async Task<ApiResponse<List<AttendanceRecordResponseModel>>> GetAllAsync(
            DateTime? fromDate, DateTime? toDate, string? department, string? userId, string? status, string? search)
        {
            var query = _context.AttendanceRecords.Include(a => a.User).AsNoTracking().AsQueryable();

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                query = query.Where(a => a.AttendanceDate >= from);
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date;
                query = query.Where(a => a.AttendanceDate <= to);
            }

            if (!string.IsNullOrWhiteSpace(department))
                query = query.Where(a => a.User != null && a.User.Department == department);

            if (!string.IsNullOrWhiteSpace(userId))
                query = query.Where(a => a.UserId == userId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(a => a.User != null &&
                                         (a.User.FullName.ToLower().Contains(term) ||
                                          (a.User.UserName != null && a.User.UserName.ToLower().Contains(term))));
            }

            var today = VnTime.Today;
            switch (status)
            {
                case AttendanceStatuses.Completed:
                    query = query.Where(a => a.CheckOutTime != null);
                    break;
                case AttendanceStatuses.Working:
                    query = query.Where(a => a.CheckOutTime == null && a.AttendanceDate >= today);
                    break;
                case AttendanceStatuses.MissingCheckout:
                    query = query.Where(a => a.CheckOutTime == null && a.AttendanceDate < today);
                    break;
                case "Manual":
                    query = query.Where(a => a.IsManualEntry);
                    break;
            }

            var rows = await query
                .OrderByDescending(a => a.AttendanceDate)
                .ThenByDescending(a => a.CheckInTime)
                .Take(5000)
                .ToListAsync();

            var managerNames = await LoadManagerNamesAsync(rows);
            var list = rows.Select(a => Map(a, a.User, ManagerName(managerNames, a))).ToList();

            return ApiResponse<List<AttendanceRecordResponseModel>>.Ok(list, "Lấy danh sách chấm công thành công.");
        }

        public async Task<ApiResponse<AttendanceRecordResponseModel>> GetByIdAsync(int id)
        {
            var record = await _context.AttendanceRecords.Include(a => a.User).AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);

            if (record == null)
                return ApiResponse<AttendanceRecordResponseModel>.Fail("Không tìm thấy bản ghi chấm công.");

            var managerNames = await LoadManagerNamesAsync(new List<AttendanceRecord> { record });
            return ApiResponse<AttendanceRecordResponseModel>.Ok(Map(record, record.User, ManagerName(managerNames, record)), "Lấy chi tiết thành công.");
        }

        public async Task<ApiResponse<bool>> DeleteAsync(int id, string? operatorUserId = null, bool isOperatorAdmin = false)
        {
            var record = await _context.AttendanceRecords.FirstOrDefaultAsync(a => a.Id == id);
            if (record == null)
                return ApiResponse<bool>.Fail("Không tìm thấy bản ghi chấm công.");

            if (!isOperatorAdmin && !string.IsNullOrEmpty(operatorUserId) && record.UserId == operatorUserId)
            {
                return ApiResponse<bool>.Fail("Để đảm bảo kiểm soát chéo, Quản lý không được tự xóa bản ghi chấm công của chính mình. Vui lòng liên hệ Admin.");
            }

            _context.AttendanceRecords.Remove(record);
            await _context.SaveChangesAsync();
            return ApiResponse<bool>.Ok(true, "Đã xóa bản ghi chấm công.");
        }

        // ------------------------------------------------------------------
        private async Task<Dictionary<string, string>> LoadManagerNamesAsync(List<AttendanceRecord> rows)
        {
            var ids = rows.Where(r => !string.IsNullOrEmpty(r.ManualEntryByUserId))
                          .Select(r => r.ManualEntryByUserId!)
                          .Distinct()
                          .ToList();
            if (ids.Count == 0) return new Dictionary<string, string>();

            return await _userManager.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName);
        }

        private static string? ManagerName(Dictionary<string, string> names, AttendanceRecord r)
            => r.ManualEntryByUserId != null && names.TryGetValue(r.ManualEntryByUserId, out var n) ? n : null;

        public static string ComputeStatus(AttendanceRecord a, DateTime today)
        {
            if (a.CheckOutTime != null) return AttendanceStatuses.Completed;

            // Nếu ngày chấm công trước ngày hôm nay: Quên Check-out
            if (a.AttendanceDate.Date < today.Date) return AttendanceStatuses.MissingCheckout;

            // Nếu là ngày hôm nay:
            var now = VnTime.Now;
            var shiftName = a.ShiftName ?? WorkShifts.HanhChinh;
            var (_, shiftEnd, _, _, _) = WorkShifts.GetSchedule(shiftName);
            var latestCheckOut = WorkShifts.GetLatestCheckOut(shiftName);

            // 1. Quá giờ đóng cổng tan ca (sau 18:00 với ca hành chính) -> Quên Check-out
            if (now.TimeOfDay > latestCheckOut)
            {
                return AttendanceStatuses.MissingCheckout;
            }

            // 2. Đã hết giờ ca nhưng còn trong khung cho phép tan ca (17:00 - 18:00) -> Hết giờ ca
            if (now.TimeOfDay > shiftEnd)
            {
                return AttendanceStatuses.ShiftEnded;
            }

            // 3. Đang trong giờ làm việc bình thường
            return AttendanceStatuses.Working;
        }

        public static AttendanceRecordResponseModel Map(AttendanceRecord a, ApplicationUser? user, string? managerName)
        {
            var entryType = a.IsManualEntry
                ? (!string.IsNullOrEmpty(a.ProofImagePath) ? "Hybrid" : "Manual")
                : "App";

            return new AttendanceRecordResponseModel
            {
                Id = a.Id,
                UserId = a.UserId,
                EmployeeName = user?.FullName ?? "N/A",
                Username = user?.UserName,
                Department = user?.Department,
                AttendanceDate = a.AttendanceDate,
                ShiftName = WorkShifts.NormalizeShiftName(a.ShiftName ?? user?.AssignedShift),
                CheckInTime = a.CheckInTime,
                CheckOutTime = a.CheckOutTime,
                TotalWorkingHours = a.TotalWorkingHours,
                AuthenticationMethod = a.AuthenticationMethod,
                IsManualEntry = a.IsManualEntry,
                ManualEntryByName = managerName,
                ProofImagePath = a.ProofImagePath,
                Remarks = a.Remarks,
                Status = ComputeStatus(a, VnTime.Today),
                EntryType = entryType,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            };
        }

        public async Task<ApiResponse<MonthlyWrappedResponseModel>> GetMonthlyWrappedAsync(string userId, int year, int month)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return ApiResponse<MonthlyWrappedResponseModel>.Fail("Không tìm thấy nhân viên.");

            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var records = await _context.AttendanceRecords
                .Where(a => a.UserId == userId && a.AttendanceDate >= startDate && a.AttendanceDate <= endDate)
                .OrderBy(a => a.AttendanceDate)
                .ToListAsync();

            var totalHours = Math.Round(records.Sum(r => r.TotalWorkingHours), 1);
            var workingDays = records.Where(r => r.CheckInTime != null).Select(r => r.AttendanceDate.Date).Distinct().Count();

            int onTimeCount = 0;
            var checkInMinutesList = new List<double>();
            foreach (var r in records.Where(r => r.CheckInTime.HasValue))
            {
                checkInMinutesList.Add(r.CheckInTime!.Value.TimeOfDay.TotalMinutes);
                var (start, _, _, _, _) = WorkShifts.GetSchedule(r.ShiftName ?? WorkShifts.HanhChinh);
                var grace = start.Add(TimeSpan.FromMinutes(15));
                if (r.CheckInTime.Value.TimeOfDay <= grace || (r.Remarks != null && r.Remarks.Contains("[Vào ca đúng giờ]")))
                {
                    onTimeCount++;
                }
            }

            var checkInRecordsCount = records.Count(r => r.CheckInTime.HasValue);
            double onTimePct = checkInRecordsCount > 0
                ? Math.Round((double)onTimeCount / checkInRecordsCount * 100, 1)
                : 100.0;

            string avgCheckInStr = "--:--";
            if (checkInMinutesList.Count > 0)
            {
                var avgMins = (int)checkInMinutesList.Average();
                avgCheckInStr = $"{avgMins / 60:D2}:{avgMins % 60:D2}";
            }

            string mostProductiveDay = "Thứ Tư";
            var dayGrouping = records
                .GroupBy(r => r.AttendanceDate.DayOfWeek)
                .Select(g => new { Day = g.Key, Total = g.Sum(x => x.TotalWorkingHours) })
                .OrderByDescending(x => x.Total)
                .FirstOrDefault();

            if (dayGrouping != null)
            {
                mostProductiveDay = dayGrouping.Day switch
                {
                    DayOfWeek.Monday => "Thứ Hai",
                    DayOfWeek.Tuesday => "Thứ Ba",
                    DayOfWeek.Wednesday => "Thứ Tư",
                    DayOfWeek.Thursday => "Thứ Năm",
                    DayOfWeek.Friday => "Thứ Sáu",
                    DayOfWeek.Saturday => "Thứ Bảy",
                    DayOfWeek.Sunday => "Chủ Nhật",
                    _ => "Thứ Tư"
                };
            }

            var favShift = records
                .GroupBy(r => r.ShiftName ?? WorkShifts.HanhChinh)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? WorkShifts.NormalizeShiftName(user.AssignedShift);

            int maxStreak = 0;
            int currentStreak = 0;
            foreach (var r in records.Where(r => r.CheckInTime.HasValue))
            {
                var (start, _, _, _, _) = WorkShifts.GetSchedule(r.ShiftName ?? WorkShifts.HanhChinh);
                var grace = start.Add(TimeSpan.FromMinutes(15));
                if (r.CheckInTime!.Value.TimeOfDay <= grace || (r.Remarks != null && r.Remarks.Contains("[Vào ca đúng giờ]")))
                {
                    currentStreak++;
                    if (currentStreak > maxStreak) maxStreak = currentStreak;
                }
                else
                {
                    currentStreak = 0;
                }
            }
            if (maxStreak == 0 && workingDays > 0) maxStreak = workingDays;

            string badge;
            string quote;
            if (workingDays == 0)
            {
                badge = "Thành Viên Mới";
                quote = "Sẵn sàng bứt phá và đồng hành cùng đại gia đình Nova Corp trong chặng đường sắp tới!";
            }
            else if (totalHours >= 170)
            {
                badge = "Cỗ Máy Năng Lượng";
                quote = "Sức làm việc bền bỉ phi thường, luôn tiên phong gánh vác mọi nhiệm vụ của công ty.";
            }
            else if (onTimePct >= 95)
            {
                badge = "Chiến Binh Đúng Giờ";
                quote = "Đúng giờ như kim đồng hồ Thụy Sĩ, tấm gương kỷ luật của toàn bộ phòng ban.";
            }
            else if (favShift.Contains("Sáng"))
            {
                badge = "Người Đón Bình Minh";
                quote = "Tràn đầy nhiệt huyết khởi đầu ngày mới cho cả đội ngũ Nova Corp.";
            }
            else
            {
                badge = "Ngôi Sao Cần Mẫn";
                quote = "Đóng góp thầm lặng nhưng vững vàng cho thành công chung của Nova Corp.";
            }

            var configs = await _context.SalaryConfigs.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
            var config = SalaryCalculator.PickConfig(configs, startDate);
            var period = PeriodHelper.Resolve(PeriodHelper.Month, year, month, null, null);

            var bonus = await _context.SalaryBonuses.AsNoTracking()
                .Where(b => b.UserId == userId && b.Year == year && b.Month == month)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();

            var salaryCalc = SalaryCalculator.Calculate(user, config, records, period, bonus?.Amount ?? 0m, bonus?.Reason);
            decimal estimatedSalary = salaryCalc.TotalSalary;

            var result = new MonthlyWrappedResponseModel
            {
                Year = year,
                Month = month,
                EmployeeName = user.FullName,
                Department = user.Department,
                TotalHours = totalHours,
                TotalWorkingDays = workingDays,
                OnTimePercentage = onTimePct,
                AverageCheckInTime = avgCheckInStr,
                MostProductiveDayOfWeek = mostProductiveDay,
                FavoriteShift = favShift,
                LongestOnTimeStreak = maxStreak,
                HonorBadge = badge,
                BadgeQuote = quote,
                TopRankingPercent = onTimePct >= 90 ? 5 : 10,
                TotalSalaryEstimated = estimatedSalary,
                BonusSalary = salaryCalc.BonusSalary,
                BonusReason = salaryCalc.BonusReason
            };

            return ApiResponse<MonthlyWrappedResponseModel>.Ok(result, "Lấy dữ liệu tổng kết tháng thành công.");
        }
    }
}
