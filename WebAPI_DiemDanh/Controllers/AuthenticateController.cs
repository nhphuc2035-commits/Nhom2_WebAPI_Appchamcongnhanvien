using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WebAPI_DiemDanh.Data;
using WebAPI_DiemDanh.Helpers;
using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthenticateController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;

        public AuthenticateController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            IConfiguration configuration,
            IServiceProvider serviceProvider)
        {
            _userManager = userManager;
            _context = context;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
        }

        private async Task<string> ResolveRoleAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains(UserRoles.Admin)) return UserRoles.Admin;
            if (roles.Contains(UserRoles.Manager)) return UserRoles.Manager;
            return UserRoles.User;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<AuthResponseModel>.Fail("Dữ liệu đầu vào không hợp lệ."));

            var input = model.Username.Trim();
            if (input == "__ping__")
                return Unauthorized(ApiResponse<AuthResponseModel>.Fail("Ping OK."));

            var user = await _userManager.FindByNameAsync(input)
                       ?? await _userManager.FindByEmailAsync(input);

            if (user == null || !await _userManager.CheckPasswordAsync(user, model.Password))
                return Unauthorized(ApiResponse<AuthResponseModel>.Fail("Tên đăng nhập hoặc mật khẩu không chính xác."));

            if (!user.IsActive)
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<AuthResponseModel>.Fail("Tài khoản này đã bị đóng/khóa. Vui lòng liên hệ quản trị viên."));

            var primaryRole = await ResolveRoleAsync(user);
            var client = model.Client?.Trim().ToLowerInvariant();

            // Phân tách rõ: Web Dashboard dành cho Admin / Quản lý; Mobile App dành cho Nhân viên chấm công
            if (client == ClientTypes.Web && primaryRole == UserRoles.User)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<AuthResponseModel>.Fail("Trang Web Quản trị chỉ dành cho Quản trị viên (Admin) và Quản lý (Manager). Nhân viên vui lòng sử dụng ứng dụng di động để điểm danh."));
            }

            if (client == ClientTypes.Mobile && primaryRole == UserRoles.Admin)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<AuthResponseModel>.Fail("Tài khoản Admin chuyên dùng trên Web Dashboard quản trị. Vui lòng đăng nhập bằng tài khoản Nhân viên trên ứng dụng di động."));
            }

            var authClaims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.UserName ?? string.Empty),
                new(ClaimTypes.Email, user.Email ?? string.Empty),
                new(ClaimTypes.GivenName, user.FullName),
                new("Department", user.Department ?? string.Empty),
                new(ClaimTypes.Role, primaryRole),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = GenerateJwtToken(authClaims);
            var expiry = DateTime.UtcNow.AddHours(Convert.ToDouble(_configuration["Jwt:ExpiryInHours"] ?? "24"));
            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            var response = new AuthResponseModel
            {
                Token = tokenString,
                Expiration = expiry,
                UserId = user.Id,
                Username = user.UserName ?? string.Empty,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                Role = primaryRole,
                Department = user.Department,
                Position = user.Position,
                AssignedShift = WorkShifts.NormalizeAssignedShift(user.AssignedShift),
                IsBiometricEnrolled = !string.IsNullOrEmpty(user.BiometricKey)
            };

            return Ok(new
            {
                status = true,
                success = true,
                message = "Đăng nhập thành công.",
                token = tokenString,
                data = response
            });
        }

        [HttpPost("init-system")]
        public async Task<IActionResult> InitSystem()
        {
            await DbInitializer.SeedRolesAndAdminAsync(_serviceProvider);
            return Ok(ApiResponse<string>.Ok("Khởi tạo hệ thống thành công. Admin: admin / Admin@123456"));
        }

        [Authorize]
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<UserDto>.Fail("Không xác định được danh tính người dùng."));

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(ApiResponse<UserDto>.Fail("Không tìm thấy thông tin người dùng."));

            var configs = await _context.SalaryConfigs.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
            var cfg = SalaryCalculator.PickConfig(configs, VnTime.Today);

            var dto = new UserDto
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber,
                Department = user.Department,
                Position = user.Position,
                AssignedShift = WorkShifts.NormalizeAssignedShift(user.AssignedShift),
                DateOfBirth = user.DateOfBirth,
                Gender = user.Gender,
                Address = user.Address,
                CitizenId = user.CitizenId,
                HireDate = user.HireDate,
                IsActive = user.IsActive,
                Role = await ResolveRoleAsync(user),
                CurrentHourlyRate = cfg?.HourlyRate ?? SalaryCalculator.ResolveStandardHourlyRateByPosition(user.Position),
                OvertimeRateMultiplier = cfg?.OvertimeRateMultiplier ?? SalaryCalculator.DefaultOvertimeMultiplier,
                StandardDailyHours = cfg?.StandardDailyHours ?? SalaryCalculator.DefaultStandardDailyHours,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                IsBiometricEnrolled = !string.IsNullOrEmpty(user.BiometricKey),
                BiometricEnrolledAt = user.BiometricEnrolledAt,
                BiometricDeviceId = user.BiometricDeviceId
            };

            return Ok(ApiResponse<UserDto>.Ok(dto, "Lấy thông tin cá nhân thành công."));
        }

        /// <summary>
        /// Nhân viên tự cập nhật thông tin liên hệ cá nhân trên Mobile App (SĐT, Ngày sinh, Giới tính, Địa chỉ, CCCD).
        /// Các mục Chức vụ, Phòng ban, Đơn giá lương do Admin quản lý.
        /// </summary>
        [Authorize]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateMyProfileModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<UserDto>.Fail("Không xác định được danh tính người dùng."));

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(ApiResponse<UserDto>.Fail("Không tìm thấy thông tin người dùng."));

            user.PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
            user.DateOfBirth = model.DateOfBirth?.Date;
            user.Gender = string.IsNullOrWhiteSpace(model.Gender) ? null : model.Gender.Trim();
            user.Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim();
            user.CitizenId = string.IsNullOrWhiteSpace(model.CitizenId) ? null : model.CitizenId.Trim();
            user.UpdatedAt = VnTime.Now;

            var res = await _userManager.UpdateAsync(user);
            if (!res.Succeeded)
                return BadRequest(ApiResponse<UserDto>.Fail("Cập nhật thông tin cá nhân thất bại."));

            return await GetProfile();
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<bool>.Fail("Dữ liệu đổi mật khẩu không hợp lệ."));

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = userId != null ? await _userManager.FindByIdAsync(userId) : null;
            if (user == null)
                return NotFound(ApiResponse<bool>.Fail("Không tìm thấy người dùng."));

            var res = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (!res.Succeeded)
                return BadRequest(ApiResponse<bool>.Fail("Mật khẩu hiện tại không đúng hoặc mật khẩu mới chưa hợp lệ."));

            return Ok(ApiResponse<bool>.Ok(true, "Đổi mật khẩu thành công."));
        }

        /// <summary>
        /// Đăng ký và liên kết khóa sinh trắc học thiết bị chính chủ vào hồ sơ nhân viên
        /// </summary>
        [Authorize]
        [HttpPost("enroll-biometric")]
        public async Task<IActionResult> EnrollBiometric([FromBody] EnrollBiometricModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<string>.Fail("Thiếu thông tin khóa sinh trắc học hoặc mã thiết bị."));

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<string>.Fail("Không xác định danh tính nhân viên."));

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound(ApiResponse<string>.Fail("Không tìm thấy nhân viên."));

            // 1. Ràng buộc: Mỗi tài khoản chỉ được gắn với 1 điện thoại duy nhất. Muốn đổi điện thoại mới phải nhờ Admin Reset.
            if (!string.IsNullOrEmpty(user.BiometricDeviceId) && !string.IsNullOrEmpty(model.DeviceId) && user.BiometricDeviceId != model.DeviceId)
            {
                return BadRequest(ApiResponse<string>.Fail(
                    "Tài khoản của bạn đã được liên kết với một thiết bị khác. Để đổi sang điện thoại mới, vui lòng liên hệ Admin / Quản lý để Reset thiết bị cũ."));
            }

            // 2. Ràng buộc: Mỗi chiếc điện thoại chỉ thuộc về 1 nhân viên duy nhất (Chống dùng chung máy điểm danh hộ).
            if (!string.IsNullOrEmpty(model.DeviceId))
            {
                var existingUserWithDevice = await _userManager.Users
                    .FirstOrDefaultAsync(u => u.BiometricDeviceId == model.DeviceId && u.Id != user.Id);

                if (existingUserWithDevice != null)
                {
                    return BadRequest(ApiResponse<string>.Fail(
                        $"Thiết bị này đã được đăng ký chính chủ cho nhân viên '{existingUserWithDevice.FullName}' ({existingUserWithDevice.UserName}). Mỗi điện thoại chỉ được liên kết cho 1 nhân viên duy nhất để chống điểm danh hộ."));
                }
            }

            user.BiometricKey = model.BiometricToken;
            user.BiometricDeviceId = model.DeviceId;
            user.BiometricEnrolledAt = VnTime.Now;
            await _userManager.UpdateAsync(user);

            return Ok(ApiResponse<string>.Ok("Đã đăng ký và liên kết vân tay sinh trắc học vào hồ sơ nhân viên thành công!"));
        }

        /// <summary>
        /// Kiểm tra trước xem thiết bị đã được đăng ký cho ai chưa (Pre-check)
        /// </summary>
        [Authorize]
        [HttpGet("check-device-ownership")]
        public async Task<IActionResult> CheckDeviceOwnership([FromQuery] string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return BadRequest(ApiResponse<object>.Fail("Thiếu mã thiết bị."));

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var existingUserWithDevice = await _userManager.Users
                .FirstOrDefaultAsync(u => u.BiometricDeviceId == deviceId);

            if (existingUserWithDevice == null)
            {
                return Ok(new { success = true, isRegistered = false, isOwnDevice = true });
            }

            var isOwn = existingUserWithDevice.Id == currentUserId;
            return Ok(new
            {
                success = true,
                isRegistered = true,
                isOwnDevice = isOwn,
                ownerName = existingUserWithDevice.FullName,
                ownerUsername = existingUserWithDevice.UserName,
                message = isOwn
                    ? "Thiết bị chính chủ của bạn."
                    : $"Thiết bị này đã được liên kết chính chủ cho nhân viên '{existingUserWithDevice.FullName}' ({existingUserWithDevice.UserName}). Mỗi điện thoại chỉ được dùng cho 1 nhân viên duy nhất để chống điểm danh hộ."
            });
        }

        /// <summary>
        /// Admin hoặc Quản lý cấp phép cho nhân viên đổi thiết bị sinh trắc học mới
        /// </summary>
        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpPost("reset-biometric/{targetUserId}")]
        public async Task<IActionResult> ResetBiometric(string targetUserId)
        {
            var user = await _userManager.FindByIdAsync(targetUserId);
            if (user == null)
                return NotFound(ApiResponse<string>.Fail("Không tìm thấy nhân viên."));

            user.BiometricKey = null;
            user.BiometricDeviceId = null;
            user.BiometricEnrolledAt = null;
            user.UpdatedAt = VnTime.Now;
            await _userManager.UpdateAsync(user);

            return Ok(ApiResponse<string>.Ok($"Đã hủy liên kết vân tay/thiết bị cũ cho nhân viên {user.FullName}. Nhân viên có thể đăng ký lại trên thiết bị mới."));
        }

        private JwtSecurityToken GenerateJwtToken(IEnumerable<Claim> claims)
        {
            var authSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? "DefaultFallbackSecretKeyMustBe32BytesLong!"));

            return new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                expires: DateTime.UtcNow.AddHours(Convert.ToDouble(_configuration["Jwt:ExpiryInHours"] ?? "24")),
                claims: claims,
                signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
            );
        }
    }
}
