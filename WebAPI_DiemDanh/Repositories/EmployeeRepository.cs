using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebAPI_DiemDanh.Data;
using WebAPI_DiemDanh.Helpers;
using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Repositories
{
    public class EmployeeRepository : IEmployeeRepository
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;

        public EmployeeRepository(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
        }

        private async Task<Dictionary<string, string>> LoadRoleMapAsync()
        {
            var map = new Dictionary<string, string>();
            // Ưu tiên quyền cao nhất nếu một người có nhiều role
            foreach (var role in new[] { UserRoles.User, UserRoles.Manager, UserRoles.Admin })
            {
                if (!await _roleManager.RoleExistsAsync(role)) continue;
                foreach (var u in await _userManager.GetUsersInRoleAsync(role))
                    map[u.Id] = role;
            }
            return map;
        }

        private static UserDto MapDto(ApplicationUser user, string role, SalaryConfig? config)
        {
            return new UserDto
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
                Role = role,
                CurrentHourlyRate = config?.HourlyRate ?? SalaryCalculator.ResolveStandardHourlyRateByPosition(user.Position),
                OvertimeRateMultiplier = config?.OvertimeRateMultiplier ?? SalaryCalculator.DefaultOvertimeMultiplier,
                StandardDailyHours = config?.StandardDailyHours ?? SalaryCalculator.DefaultStandardDailyHours,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                IsBiometricEnrolled = !string.IsNullOrEmpty(user.BiometricKey),
                BiometricEnrolledAt = user.BiometricEnrolledAt,
                BiometricDeviceId = user.BiometricDeviceId
            };
        }

        private async Task<SalaryConfig?> LatestConfigAsync(string userId)
        {
            var configs = await _context.SalaryConfigs.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
            return SalaryCalculator.PickConfig(configs, VnTime.Today);
        }

        private async Task<string> GetRoleAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains(UserRoles.Admin)) return UserRoles.Admin;
            if (roles.Contains(UserRoles.Manager)) return UserRoles.Manager;
            return UserRoles.User;
        }

        public async Task<ApiResponse<List<UserDto>>> GetAllEmployeesAsync(string? searchTerm, string? department, bool? isActive, string? role)
        {
            var query = _userManager.Users.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(u => u.FullName.ToLower().Contains(term)
                                      || (u.UserName != null && u.UserName.ToLower().Contains(term))
                                      || (u.Email != null && u.Email.ToLower().Contains(term))
                                      || (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(department))
                query = query.Where(u => u.Department == department);

            if (isActive.HasValue)
                query = query.Where(u => u.IsActive == isActive.Value);

            var users = await query.OrderBy(u => u.FullName).ToListAsync();
            var roleMap = await LoadRoleMapAsync();

            var allConfigs = await _context.SalaryConfigs.AsNoTracking().ToListAsync();
            var configsByUser = allConfigs.GroupBy(c => c.UserId).ToDictionary(g => g.Key, g => g.ToList());
            var today = VnTime.Today;

            var list = users
                .Select(u =>
                {
                    var r = roleMap.TryGetValue(u.Id, out var rr) ? rr : UserRoles.User;
                    var cfg = configsByUser.TryGetValue(u.Id, out var cs) ? SalaryCalculator.PickConfig(cs, today) : null;
                    return MapDto(u, r, cfg);
                })
                .Where(d => string.IsNullOrWhiteSpace(role) || string.Equals(d.Role, role, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return ApiResponse<List<UserDto>>.Ok(list, "Lấy danh sách nhân sự thành công.");
        }

        public async Task<ApiResponse<UserDto>> GetEmployeeByIdAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return ApiResponse<UserDto>.Fail("Không tìm thấy nhân viên.");

            return ApiResponse<UserDto>.Ok(MapDto(user, await GetRoleAsync(user), await LatestConfigAsync(id)), "Lấy thông tin nhân viên thành công.");
        }

        public async Task<ApiResponse<List<string>>> GetDepartmentsAsync()
        {
            var list = await _userManager.Users.AsNoTracking()
                .Where(u => u.Department != null && u.Department != "")
                .Select(u => u.Department!)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();
            return ApiResponse<List<string>>.Ok(list, "Lấy danh sách phòng ban thành công.");
        }

        public async Task<ApiResponse<UserDto>> CreateEmployeeAsync(RegistrationModel model)
        {
            var username = model.Username.Trim();
            var email = model.Email.Trim();

            if (await _userManager.FindByNameAsync(username) != null)
                return ApiResponse<UserDto>.Fail("Tên đăng nhập này đã được sử dụng.");

            if (await _userManager.FindByEmailAsync(email) != null)
                return ApiResponse<UserDto>.Fail("Địa chỉ email này đã được sử dụng.");

            var now = VnTime.Now;
            var newUser = new ApplicationUser
            {
                UserName = username,
                Email = email,
                FullName = model.FullName.Trim(),
                Department = string.IsNullOrWhiteSpace(model.Department) ? null : model.Department.Trim(),
                Position = string.IsNullOrWhiteSpace(model.Position) ? null : model.Position.Trim(),
                AssignedShift = WorkShifts.NormalizeAssignedShift(model.AssignedShift),
                PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim(),
                DateOfBirth = model.DateOfBirth?.Date,
                Gender = string.IsNullOrWhiteSpace(model.Gender) ? null : model.Gender.Trim(),
                Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim(),
                CitizenId = string.IsNullOrWhiteSpace(model.CitizenId) ? null : model.CitizenId.Trim(),
                HireDate = model.HireDate?.Date ?? now.Date,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = now
            };

            var result = await _userManager.CreateAsync(newUser, model.Password);
            if (!result.Succeeded)
                return ApiResponse<UserDto>.Fail("Tạo tài khoản thất bại: " + string.Join(" ", result.Errors.Select(e => e.Description)),
                    result.Errors.Select(e => e.Description).ToList());

            var targetRole = UserRoles.Normalize(model.Role);
            if (!await _roleManager.RoleExistsAsync(targetRole))
                await _roleManager.CreateAsync(new IdentityRole(targetRole));
            await _userManager.AddToRoleAsync(newUser, targetRole);

            var resolvedHourlyRate = model.InitialHourlyRate > 0
                ? model.InitialHourlyRate
                : SalaryCalculator.ResolveStandardHourlyRateByPosition(newUser.Position);

            var config = new SalaryConfig
            {
                UserId = newUser.Id,
                HourlyRate = resolvedHourlyRate,
                OvertimeRateMultiplier = model.OvertimeRateMultiplier >= 1 ? model.OvertimeRateMultiplier : SalaryCalculator.DefaultOvertimeMultiplier,
                StandardDailyHours = model.StandardDailyHours >= 1 ? model.StandardDailyHours : SalaryCalculator.DefaultStandardDailyHours,
                EffectiveDate = now.Date,
                Note = $"Định mức lương theo chức vụ ({newUser.Position ?? "Nhân viên"})",
                CreatedAt = now
            };
            _context.SalaryConfigs.Add(config);
            await _context.SaveChangesAsync();

            return ApiResponse<UserDto>.Ok(MapDto(newUser, targetRole, config), $"Đã tạo tài khoản cho {newUser.FullName}.");
        }

        public async Task<ApiResponse<UserDto>> UpdateEmployeeAsync(string id, UpdateUserModel model, string currentUserId)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return ApiResponse<UserDto>.Fail("Không tìm thấy nhân viên.");

            var now = VnTime.Now;

            if (!string.IsNullOrWhiteSpace(model.Username) && !string.Equals(model.Username.Trim(), user.UserName, StringComparison.OrdinalIgnoreCase))
            {
                var newUname = model.Username.Trim();
                var existingUser = await _userManager.FindByNameAsync(newUname);
                if (existingUser != null && existingUser.Id != user.Id)
                    return ApiResponse<UserDto>.Fail($"Tên đăng nhập '{newUname}' đã tồn tại trong hệ thống.");
                user.UserName = newUname;
                user.NormalizedUserName = _userManager.NormalizeName(newUname);
            }

            if (!string.IsNullOrWhiteSpace(model.Email) && !string.Equals(model.Email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
            {
                var owner = await _userManager.FindByEmailAsync(model.Email.Trim());
                if (owner != null && owner.Id != user.Id)
                    return ApiResponse<UserDto>.Fail("Email đã được tài khoản khác sử dụng.");
                user.Email = model.Email.Trim();
                user.NormalizedEmail = _userManager.NormalizeEmail(user.Email);
            }

            user.FullName = model.FullName.Trim();
            user.Department = string.IsNullOrWhiteSpace(model.Department) ? null : model.Department.Trim();
            user.Position = string.IsNullOrWhiteSpace(model.Position) ? null : model.Position.Trim();
            if (!string.IsNullOrWhiteSpace(model.AssignedShift))
                user.AssignedShift = WorkShifts.NormalizeAssignedShift(model.AssignedShift);
            user.PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
            user.DateOfBirth = model.DateOfBirth?.Date;
            user.Gender = string.IsNullOrWhiteSpace(model.Gender) ? null : model.Gender.Trim();
            user.Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim();
            user.CitizenId = string.IsNullOrWhiteSpace(model.CitizenId) ? null : model.CitizenId.Trim();
            if (model.HireDate.HasValue)
                user.HireDate = model.HireDate.Value.Date;
            user.UpdatedAt = now;

            var update = await _userManager.UpdateAsync(user);
            if (!update.Succeeded)
                return ApiResponse<UserDto>.Fail("Cập nhật thất bại: " + string.Join(" ", update.Errors.Select(e => e.Description)));

            // Đổi vai trò
            var currentRole = await GetRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(model.Role))
            {
                var newRole = UserRoles.Normalize(model.Role);
                if (newRole != currentRole)
                {
                    if (user.Id == currentUserId)
                        return ApiResponse<UserDto>.Fail("Bạn không thể tự thay đổi vai trò của chính mình.");

                    if (currentRole == UserRoles.Admin && (await _userManager.GetUsersInRoleAsync(UserRoles.Admin)).Count <= 1)
                        return ApiResponse<UserDto>.Fail("Hệ thống phải còn ít nhất 1 tài khoản Admin.");

                    if (!await _roleManager.RoleExistsAsync(newRole))
                        await _roleManager.CreateAsync(new IdentityRole(newRole));

                    var oldRoles = await _userManager.GetRolesAsync(user);
                    await _userManager.RemoveFromRolesAsync(user, oldRoles);
                    await _userManager.AddToRoleAsync(user, newRole);
                    currentRole = newRole;
                }
            }

            // Đặt lại mật khẩu
            if (!string.IsNullOrWhiteSpace(model.NewPassword))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var reset = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
                if (!reset.Succeeded)
                    return ApiResponse<UserDto>.Fail("Đặt lại mật khẩu thất bại: " + string.Join(" ", reset.Errors.Select(e => e.Description)));
            }

            // Cập nhật cấu hình lương (lưu thành bản ghi mới để giữ lịch sử)
            var latest = await LatestConfigAsync(id);
            if (model.HourlyRate.HasValue || model.OvertimeRateMultiplier.HasValue || model.StandardDailyHours.HasValue)
            {
                var rate = model.HourlyRate ?? latest?.HourlyRate ?? SalaryCalculator.DefaultHourlyRate;
                var mul = model.OvertimeRateMultiplier ?? latest?.OvertimeRateMultiplier ?? SalaryCalculator.DefaultOvertimeMultiplier;
                var std = model.StandardDailyHours ?? latest?.StandardDailyHours ?? SalaryCalculator.DefaultStandardDailyHours;

                var changed = latest == null || latest.HourlyRate != rate || latest.OvertimeRateMultiplier != mul || Math.Abs(latest.StandardDailyHours - std) > 0.001;
                if (changed)
                {
                    latest = new SalaryConfig
                    {
                        UserId = id,
                        HourlyRate = rate,
                        OvertimeRateMultiplier = mul,
                        StandardDailyHours = std,
                        EffectiveDate = now,
                        Note = "Cập nhật từ trang quản lý nhân sự",
                        CreatedAt = now
                    };
                    _context.SalaryConfigs.Add(latest);
                    await _context.SaveChangesAsync();
                }
            }

            return ApiResponse<UserDto>.Ok(MapDto(user, currentRole, latest), $"Đã cập nhật thông tin {user.FullName}.");
        }

        public async Task<ApiResponse<bool>> ToggleEmployeeStatusAsync(string id, bool isActive, string currentUserId)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return ApiResponse<bool>.Fail("Không tìm thấy nhân viên.");

            if (!isActive && user.Id == currentUserId)
                return ApiResponse<bool>.Fail("Bạn không thể tự khóa tài khoản của chính mình.");

            user.IsActive = isActive;
            user.UpdatedAt = VnTime.Now;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return ApiResponse<bool>.Fail("Không thể thay đổi trạng thái tài khoản.", result.Errors.Select(e => e.Description).ToList());

            return ApiResponse<bool>.Ok(true, isActive ? $"Đã mở khóa tài khoản {user.FullName}." : $"Đã đóng (khóa) tài khoản {user.FullName}.");
        }

        public async Task<ApiResponse<bool>> DeleteEmployeeAsync(string id, string currentUserId)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return ApiResponse<bool>.Fail("Không tìm thấy nhân viên.");

            if (user.Id == currentUserId)
                return ApiResponse<bool>.Fail("Bạn không thể xóa tài khoản của chính mình.");

            if (await _userManager.IsInRoleAsync(user, UserRoles.Admin) && (await _userManager.GetUsersInRoleAsync(UserRoles.Admin)).Count <= 1)
                return ApiResponse<bool>.Fail("Không thể xóa Admin cuối cùng của hệ thống.");

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
                return ApiResponse<bool>.Fail("Xóa tài khoản thất bại.", result.Errors.Select(e => e.Description).ToList());

            return ApiResponse<bool>.Ok(true, $"Đã xóa vĩnh viễn tài khoản {user.FullName} và toàn bộ dữ liệu chấm công liên quan.");
        }
    }
}
