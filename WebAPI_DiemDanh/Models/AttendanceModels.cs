using System.ComponentModel.DataAnnotations;

namespace WebAPI_DiemDanh.Models
{
    public static class AuthMethods
    {
        public const string Fingerprint = "Fingerprint";         // Vân tay
        public const string FaceRecognition = "FaceRecognition"; // Khuôn mặt
        public const string NfcCard = "NfcCard";                 // Thẻ NFC
        public const string Manual = "Manual";                   // Thủ công
    }

    public static class AttendanceStatuses
    {
        public const string Working = "Working";                 // Đang trong ca (đã vào, chưa ra)
        public const string Completed = "Completed";             // Đã hoàn thành ca
        public const string MissingCheckout = "MissingCheckout"; // Quên check-out (ngày cũ hoặc ca hôm nay đã quá giờ đóng cổng)
        public const string ShiftEnded = "ShiftEnded";           // Hết giờ làm việc (trong khung 60 phút chờ ra về)
        public const string NotCheckedIn = "NotCheckedIn";       // Chưa vào ca
    }

    public static class WorkShifts
    {
        public const string HanhChinh = "Ca Hành Chính (08:00 - 17:00)";
        public const string CaSang = "Ca Sáng (06:00 - 12:00)";
        public const string CaChieu = "Ca Chiều (14:00 - 22:00)";
        public const string CaToi = "Ca Tối (18:00 - 22:00)";
        public const string XoayCa = "Xoay ca linh hoạt";

        public static readonly string[] AllShiftNames =
        {
            HanhChinh,
            CaSang,
            CaChieu,
            CaToi
        };

        public static string NormalizeShiftName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return HanhChinh;
            var s = raw.Trim();
            if (s.Contains("Sáng", StringComparison.OrdinalIgnoreCase) || s.Contains("06:00", StringComparison.OrdinalIgnoreCase))
                return CaSang;
            if (s.Contains("Chiều", StringComparison.OrdinalIgnoreCase) || s.Contains("14:00 - 22:00", StringComparison.OrdinalIgnoreCase))
                return CaChieu;
            if (s.Contains("Tối", StringComparison.OrdinalIgnoreCase) || s.Contains("18:00", StringComparison.OrdinalIgnoreCase))
                return CaToi;
            return HanhChinh;
        }

        public static string NormalizeAssignedShift(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return HanhChinh;
            if (raw.Contains("Xoay ca", StringComparison.OrdinalIgnoreCase) || raw.Contains("Linh hoạt", StringComparison.OrdinalIgnoreCase))
                return XoayCa;
            return NormalizeShiftName(raw);
        }

        public static (TimeSpan Start, TimeSpan End, TimeSpan EarliestCheckIn, TimeSpan LatestCheckIn, double StandardHours) GetSchedule(string shiftName)
        {
            var normalized = NormalizeShiftName(shiftName);
            return normalized switch
            {
                CaSang => (new TimeSpan(6, 0, 0), new TimeSpan(12, 0, 0), new TimeSpan(5, 15, 0), new TimeSpan(8, 30, 0), 6.0),
                CaChieu => (new TimeSpan(14, 0, 0), new TimeSpan(22, 0, 0), new TimeSpan(12, 0, 0), new TimeSpan(17, 0, 0), 8.0),
                CaToi => (new TimeSpan(18, 0, 0), new TimeSpan(22, 0, 0), new TimeSpan(17, 30, 0), new TimeSpan(21, 0, 0), 4.0),
                _ => (new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), new TimeSpan(7, 15, 0), new TimeSpan(10, 30, 0), 8.0)
            };
        }

        public static TimeSpan GetLatestCheckOut(string shiftName)
        {
            var normalized = NormalizeShiftName(shiftName);
            return normalized switch
            {
                CaSang => new TimeSpan(12, 15, 0),      // Ca sáng kết thúc 12:00, đóng cổng tan ca lúc 12:15
                CaChieu => new TimeSpan(22, 30, 0),     // Ca chiều kết thúc 22:00, đóng cổng tan ca lúc 22:30
                CaToi => new TimeSpan(22, 30, 0),        // Ca tối kết thúc 22:00, đóng cổng tan ca lúc 22:30
                _ => new TimeSpan(18, 0, 0)             // Ca hành chính kết thúc 17:00, đóng cổng tan ca lúc 18:00
            };
        }
    }

    /// <summary>
    /// Yêu cầu Check-in từ App. Danh tính nhân viên lấy từ JWT, thời gian lấy từ SERVER.
    /// </summary>
    public class CheckInRequestModel
    {
        /// <summary>Không bắt buộc — server luôn dùng UserId trong token.</summary>
        public string? UserId { get; set; }

        /// <summary>Bị bỏ qua — server dùng giờ chuẩn Việt Nam để chống chỉnh giờ điện thoại.</summary>
        public DateTime? CheckInTime { get; set; }

        /// <summary>Ca làm việc nhân viên chọn khi chấm công.</summary>
        public string? ShiftName { get; set; }

        public string AuthenticationMethod { get; set; } = AuthMethods.Fingerprint;

        /// <summary>Khóa sinh trắc học được mở bằng vân tay trong chip bảo mật của điện thoại.</summary>
        public string? BiometricToken { get; set; }

        /// <summary>Mã định danh thiết bị chính chủ.</summary>
        public string? DeviceId { get; set; }

        /// <summary>Ảnh Selfie bằng chứng lúc bấm vào ca (Base64 chuỗi ảnh JPEG)</summary>
        public string? ProofImageBase64 { get; set; }

        public string? Remarks { get; set; }
    }

    public class CheckOutRequestModel
    {
        public string? UserId { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public string AuthenticationMethod { get; set; } = AuthMethods.Fingerprint;
        public string? BiometricToken { get; set; }
        public string? DeviceId { get; set; }
        public string? Remarks { get; set; }
    }

    public class ManualAttendanceModel
    {
        [Required(ErrorMessage = "Mã nhân viên là bắt buộc")]
        public string UserId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ngày chấm công là bắt buộc")]
        public DateTime AttendanceDate { get; set; }

        [Required(ErrorMessage = "Giờ vào làm là bắt buộc")]
        public DateTime CheckInTime { get; set; }

        /// <summary>Giờ ra về. Bỏ trống = chỉ ghi nhận giờ vào (nhân viên đang trong ca).</summary>
        public DateTime? CheckOutTime { get; set; }

        /// <summary>Ca làm việc áp dụng cho bản ghi chấm công thủ công</summary>
        public string? ShiftName { get; set; }

        [Required(ErrorMessage = "Lý do chấm công thủ công là bắt buộc")]
        [MinLength(3, ErrorMessage = "Lý do tối thiểu 3 ký tự")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>Cho phép ghi đè bản ghi cũ nếu trong ngày đã có bản ghi</summary>
        public bool OverrideExisting { get; set; } = true;
    }

    public class AttendanceRecordResponseModel
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? Department { get; set; }
        public DateTime AttendanceDate { get; set; }
        public string ShiftName { get; set; } = WorkShifts.HanhChinh;
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public double TotalWorkingHours { get; set; }
        public string AuthenticationMethod { get; set; } = string.Empty;
        public bool IsManualEntry { get; set; }
        public string? ManualEntryByName { get; set; }
        public string? ProofImagePath { get; set; }
        public string? Remarks { get; set; }
        public string Status { get; set; } = AttendanceStatuses.Working;
        public string EntryType { get; set; } = "App"; // App | Hybrid | Manual
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Mô hình dữ liệu Tổng Kết Tháng phong cách Spotify (Monthly Work Wrapped)
    /// </summary>
    public class MonthlyWrappedResponseModel
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string? Department { get; set; }
        public double TotalHours { get; set; }
        public int TotalWorkingDays { get; set; }
        public double OnTimePercentage { get; set; }
        public string AverageCheckInTime { get; set; } = "08:00";
        public string MostProductiveDayOfWeek { get; set; } = "Thứ Tư";
        public string FavoriteShift { get; set; } = WorkShifts.HanhChinh;
        public int LongestOnTimeStreak { get; set; }
        public string HonorBadge { get; set; } = "Chiến Binh Đúng Giờ";
        public string BadgeQuote { get; set; } = "Luôn có mặt trước tiếng chuông reo và hoàn thành xuất sắc mọi ca làm việc.";
        public int TopRankingPercent { get; set; } = 5;
        public decimal TotalSalaryEstimated { get; set; }
        public decimal BonusSalary { get; set; }
        public string? BonusReason { get; set; }
    }

    /// <summary>Trạng thái ca làm hôm nay của nhân viên (dùng cho màn hình chính App).</summary>
    public class TodayAttendanceStatusModel
    {
        public DateTime ServerTime { get; set; }
        public DateTime Today { get; set; }
        public string Status { get; set; } = AttendanceStatuses.NotCheckedIn;
        public bool CanCheckIn { get; set; }
        public bool CanCheckOut { get; set; }
        public bool IsBiometricEnrolled { get; set; }
        public double StandardDailyHours { get; set; } = 8.0;
        public string AssignedShift { get; set; } = WorkShifts.HanhChinh;
        public AttendanceRecordResponseModel? Record { get; set; }

        /// <summary>Bản ghi ngày cũ chưa check-out (nếu có) — cần quản lý chấm bù.</summary>
        public AttendanceRecordResponseModel? PendingMissingCheckout { get; set; }
    }
}
