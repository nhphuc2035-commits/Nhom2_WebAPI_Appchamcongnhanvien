using System.ComponentModel.DataAnnotations;

namespace WebAPI_DiemDanh.Models
{
    public static class ClientTypes
    {
        public const string Web = "web";
        public const string Mobile = "mobile";
    }

    public class LoginModel
    {
        [Required(ErrorMessage = "Tên đăng nhập hoặc Email là bắt buộc")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu là bắt buộc")]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// "web" (Dashboard cho Admin/Quản lý) | "mobile" (App cho nhân viên). Bỏ trống = không giới hạn (Swagger).
        /// </summary>
        public string? Client { get; set; }
    }

    public class RegistrationModel
    {
        [Required(ErrorMessage = "Tên đăng nhập là bắt buộc")]
        [RegularExpression(@"^[a-zA-Z0-9._@-]{3,50}$", ErrorMessage = "Tên đăng nhập 3-50 ký tự, chỉ gồm chữ không dấu, số và . _ - @")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email là bắt buộc")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu là bắt buộc")]
        [MinLength(6, ErrorMessage = "Mật khẩu tối thiểu 6 ký tự")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Họ và tên là bắt buộc")]
        public string FullName { get; set; } = string.Empty;

        public string? Department { get; set; }
        public string? Position { get; set; }
        public string? AssignedShift { get; set; }
        public string? PhoneNumber { get; set; }

        // Thông tin hồ sơ nhân sự chi tiết
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Address { get; set; }
        public string? CitizenId { get; set; }
        public DateTime? HireDate { get; set; }

        [Range(0, 100000000, ErrorMessage = "Mức lương giờ không hợp lệ")]
        public decimal InitialHourlyRate { get; set; } = 0m;

        [Range(1.0, 5.0, ErrorMessage = "Hệ số tăng ca từ 1.0 đến 5.0")]
        public decimal OvertimeRateMultiplier { get; set; } = 1.5m;

        [Range(1.0, 24.0, ErrorMessage = "Giờ chuẩn/ngày từ 1 đến 24")]
        public double StandardDailyHours { get; set; } = 8.0;

        /// <summary>"Admin" | "Manager" | "User". Mặc định "User"</summary>
        public string Role { get; set; } = UserRoles.User;
    }

    public class UpdateUserModel
    {
        public string? Username { get; set; }

        [Required(ErrorMessage = "Họ và tên là bắt buộc")]
        public string FullName { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        public string? Email { get; set; }

        public string? Department { get; set; }
        public string? Position { get; set; }
        public string? AssignedShift { get; set; }
        public string? PhoneNumber { get; set; }

        // Thông tin hồ sơ nhân sự chi tiết
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Address { get; set; }
        public string? CitizenId { get; set; }
        public DateTime? HireDate { get; set; }

        /// <summary>Đổi vai trò (Admin | Manager | User). Bỏ trống = giữ nguyên.</summary>
        public string? Role { get; set; }

        /// <summary>Đặt lại mật khẩu mới cho nhân viên. Bỏ trống = giữ nguyên.</summary>
        public string? NewPassword { get; set; }

        /// <summary>Cập nhật cấu hình lương (bỏ trống = giữ nguyên). Mỗi lần đổi sẽ lưu lịch sử.</summary>
        [Range(0, 100000000, ErrorMessage = "Mức lương giờ không hợp lệ")]
        public decimal? HourlyRate { get; set; }

        [Range(1.0, 5.0, ErrorMessage = "Hệ số tăng ca từ 1.0 đến 5.0")]
        public decimal? OvertimeRateMultiplier { get; set; }

        [Range(1.0, 24.0, ErrorMessage = "Giờ chuẩn/ngày từ 1 đến 24")]
        public double? StandardDailyHours { get; set; }
    }

    /// <summary>
    /// Cho phép nhân viên tự cập nhật thông tin liên hệ cá nhân trên Mobile App
    /// (Không được tự ý sửa Chức vụ, Phòng ban, Mức lương - các mục này do Admin quản lý).
    /// </summary>
    public class UpdateMyProfileModel
    {
        public string? PhoneNumber { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Address { get; set; }
        public string? CitizenId { get; set; }
    }

    public class UserStatusUpdateModel
    {
        public bool IsActive { get; set; }
    }

    public class ChangePasswordModel
    {
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
        [MinLength(6, ErrorMessage = "Mật khẩu mới tối thiểu 6 ký tự")]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class EnrollBiometricModel
    {
        [Required(ErrorMessage = "Mã sinh trắc học là bắt buộc")]
        public string BiometricToken { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã thiết bị là bắt buộc")]
        public string DeviceId { get; set; } = string.Empty;
    }

    public class AuthResponseModel
    {
        public string Token { get; set; } = string.Empty;
        public DateTime Expiration { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? Position { get; set; }
        public string? AssignedShift { get; set; }
        public bool IsBiometricEnrolled { get; set; }
    }

    public class UserDto
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? Department { get; set; }
        public string? Position { get; set; }
        public string? AssignedShift { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Address { get; set; }
        public string? CitizenId { get; set; }
        public DateTime? HireDate { get; set; }
        public bool IsActive { get; set; }
        public string Role { get; set; } = string.Empty;
        public decimal? CurrentHourlyRate { get; set; }
        public decimal? OvertimeRateMultiplier { get; set; }
        public double? StandardDailyHours { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsBiometricEnrolled { get; set; }
        public DateTime? BiometricEnrolledAt { get; set; }
        public string? BiometricDeviceId { get; set; }
    }

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
        public List<string>? Errors { get; set; }

        public static ApiResponse<T> Ok(T data, string message = "Thành công") =>
            new() { Success = true, Message = message, Data = data };

        public static ApiResponse<T> Fail(string message, List<string>? errors = null) =>
            new() { Success = false, Message = message, Errors = errors };
    }
}
