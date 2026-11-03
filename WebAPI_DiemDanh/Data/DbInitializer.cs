using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebAPI_DiemDanh.Helpers;
using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Data
{
    public static class DbInitializer
    {
        public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            // 0. Đảm bảo bảng AspNetUsers và AttendanceRecords có đầy đủ các cột hồ sơ nhân sự & ca làm việc
            const string ensureColumnsSql = @"
IF COL_LENGTH('dbo.AspNetUsers', 'DateOfBirth') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD DateOfBirth DATETIME2 NULL;
IF COL_LENGTH('dbo.AspNetUsers', 'Gender') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD Gender NVARCHAR(20) NULL;
IF COL_LENGTH('dbo.AspNetUsers', 'Address') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD Address NVARCHAR(300) NULL;
IF COL_LENGTH('dbo.AspNetUsers', 'CitizenId') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD CitizenId NVARCHAR(25) NULL;
IF COL_LENGTH('dbo.AspNetUsers', 'HireDate') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD HireDate DATETIME2 NULL;
IF COL_LENGTH('dbo.AspNetUsers', 'AssignedShift') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD AssignedShift NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.AttendanceRecords', 'ShiftName') IS NULL
    ALTER TABLE dbo.AttendanceRecords ADD ShiftName NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.AttendanceRecords', 'ProofImagePath') IS NULL
    ALTER TABLE dbo.AttendanceRecords ADD ProofImagePath NVARCHAR(500) NULL;";

            await context.Database.ExecuteSqlRawAsync(ensureColumnsSql);
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.AttendanceRecords SET ShiftName = N'Ca Hành Chính (08:00 - 17:00)' WHERE ShiftName IS NULL;");

            // 1. Tạo đầy đủ 3 roles: Admin, Manager, User
            foreach (var roleName in UserRoles.All)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            var now = VnTime.Now;

            // 2. Tài khoản Admin mặc định
            var adminEmail = "admin@company.com";
            var defaultAdmin = await userManager.FindByEmailAsync(adminEmail)
                               ?? await userManager.FindByNameAsync("admin");
            if (defaultAdmin == null)
            {
                var adminUser = new ApplicationUser
                {
                    UserName = "admin",
                    Email = adminEmail,
                    FullName = "Quản Trị Viên Hệ Thống",
                    Department = "Ban Giám Đốc",
                    Position = "Giám Đốc Điều Hành",
                    PhoneNumber = "0901234567",
                    DateOfBirth = new DateTime(1988, 5, 15),
                    Gender = "Nam",
                    CitizenId = "079088001234",
                    Address = "Số 12 Nguyễn Huệ, Quận 1, TP. Hồ Chí Minh",
                    HireDate = new DateTime(2020, 1, 10),
                    EmailConfirmed = true,
                    IsActive = true,
                    CreatedAt = now
                };

                var createAdmin = await userManager.CreateAsync(adminUser, "Admin@123456");
                if (createAdmin.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, UserRoles.Admin);
                    context.SalaryConfigs.Add(new SalaryConfig
                    {
                        UserId = adminUser.Id,
                        HourlyRate = 150000m,
                        OvertimeRateMultiplier = 1.5m,
                        StandardDailyHours = 8.0,
                        EffectiveDate = now.Date,
                        Note = "Lương quản trị viên mặc định",
                        CreatedAt = now
                    });
                    await context.SaveChangesAsync();
                }
            }
            else if (!await userManager.IsInRoleAsync(defaultAdmin, UserRoles.Admin))
            {
                await userManager.AddToRoleAsync(defaultAdmin, UserRoles.Admin);
            }

            // 3. Tài khoản Quản lý (Manager) mặc định
            var managers = await userManager.GetUsersInRoleAsync(UserRoles.Manager);
            if (managers.Count == 0)
            {
                var existingHr = await userManager.FindByNameAsync("tran.thi.bich");
                if (existingHr != null)
                {
                    var oldRoles = await userManager.GetRolesAsync(existingHr);
                    await userManager.RemoveFromRolesAsync(existingHr, oldRoles);
                    await userManager.AddToRoleAsync(existingHr, UserRoles.Manager);
                    existingHr.Position = "Trưởng Phòng Nhân Sự";
                    await userManager.UpdateAsync(existingHr);
                }
            }

            // 4. Bổ sung thông tin hồ sơ chi tiết (Ngày sinh, Giới tính, CCCD, Địa chỉ, Ngày vào làm, Chức vụ & Lương theo chức vụ) cho các nhân sự hiện có nếu còn trống
            var allUsers = await userManager.Users.ToListAsync();
            var sampleProfiles = new Dictionary<string, (string Dept, string Pos, string Phone, DateTime Dob, string Gender, string Cccd, string Addr, DateTime Hire, decimal Rate)>(StringComparer.OrdinalIgnoreCase)
            {
                ["admin"] = ("Ban Giám Đốc", "Giám Đốc Điều Hành", "0901234567", new DateTime(1988, 5, 15), "Nam", "079088001234", "12 Nguyễn Huệ, Quận 1, TP.HCM", new DateTime(2020, 1, 10), 150000m),
                ["tran.thi.bich"] = ("Phòng Nhân Sự", "Trưởng Phòng", "0912345678", new DateTime(1992, 8, 20), "Nữ", "079192004567", "45 Lê Lợi, Quận 1, TP.HCM", new DateTime(2021, 3, 1), 110000m),
                ["le.hoang.cuong"] = ("Phòng Kỹ Thuật", "Trưởng Nhóm (Team Lead)", "0987654321", new DateTime(1994, 11, 12), "Nam", "079094007890", "210 Điện Biên Phủ, Quận Bình Thạnh, TP.HCM", new DateTime(2021, 6, 15), 90000m),
                ["nguyen.van.an"] = ("Phòng Kỹ Thuật", "Chuyên Viên Cao Cấp (Senior)", "0938112233", new DateTime(1996, 3, 25), "Nam", "079096003344", "88 Cách Mạng Tháng 8, Quận 3, TP.HCM", new DateTime(2022, 2, 10), 75000m),
                ["pham.minh.duc"] = ("Phòng Kỹ Thuật", "Nhân Viên Chính Thức", "0977223344", new DateTime(1998, 7, 9), "Nam", "079098005566", "156 Hoàng Hoa Thám, Quận Tân Bình, TP.HCM", new DateTime(2023, 1, 5), 55000m),
                ["hoang.thi.em"] = ("Phòng Kế Toán", "Nhân Viên Chính Thức", "0966334455", new DateTime(1997, 12, 3), "Nữ", "079197006677", "34 Phan Xích Long, Quận Phú Nhuận, TP.HCM", new DateTime(2022, 9, 12), 55000m),
                ["vu.quoc.huy"] = ("Phòng Kinh Doanh", "Chuyên Viên Cao Cấp (Senior)", "0944556677", new DateTime(1995, 4, 18), "Nam", "079095008899", "102 Nguyễn Thị Minh Khai, Quận 3, TP.HCM", new DateTime(2021, 11, 1), 75000m),
                ["dang.thu.thao"] = ("Phòng Marketing", "Nhân Viên Chính Thức", "0933667788", new DateTime(1999, 9, 28), "Nữ", "079199001122", "72 Võ Văn Tần, Quận 3, TP.HCM", new DateTime(2023, 5, 20), 55000m),
                ["bui.anh.tuan"] = ("Phòng Kỹ Thuật", "Nhân Viên Thử Việc", "0922778899", new DateTime(2001, 2, 14), "Nam", "079201003355", "19 Cộng Hòa, Quận Tân Bình, TP.HCM", new DateTime(2024, 8, 1), 42000m),
                ["do.ngoc.lan"] = ("Phòng Nhân Sự", "Thực Tập Sinh (Intern)", "0911889900", new DateTime(2003, 6, 30), "Nữ", "079303007788", "55 Nguyễn Văn Cừ, Quận 5, TP.HCM", new DateTime(2024, 10, 1), 35000m),
            };

            int idx = 1;
            foreach (var u in allUsers)
            {
                bool changed = false;
                var uname = u.UserName ?? string.Empty;

                if (sampleProfiles.TryGetValue(uname, out var sample))
                {
                    if (string.IsNullOrWhiteSpace(u.Department)) { u.Department = sample.Dept; changed = true; }
                    if (string.IsNullOrWhiteSpace(u.Position) || u.Position == "Nhân viên") { u.Position = sample.Pos; changed = true; }
                    if (string.IsNullOrWhiteSpace(u.PhoneNumber)) { u.PhoneNumber = sample.Phone; changed = true; }
                    if (!u.DateOfBirth.HasValue) { u.DateOfBirth = sample.Dob; changed = true; }
                    if (string.IsNullOrWhiteSpace(u.Gender)) { u.Gender = sample.Gender; changed = true; }
                    if (string.IsNullOrWhiteSpace(u.CitizenId)) { u.CitizenId = sample.Cccd; changed = true; }
                    if (string.IsNullOrWhiteSpace(u.Address)) { u.Address = sample.Addr; changed = true; }
                    if (!u.HireDate.HasValue) { u.HireDate = sample.Hire; changed = true; }
                }
                else
                {
                    if (!u.DateOfBirth.HasValue) { u.DateOfBirth = new DateTime(1996, (idx % 12) + 1, (idx % 25) + 1); changed = true; }
                    if (string.IsNullOrWhiteSpace(u.Gender)) { u.Gender = (idx % 2 == 0) ? "Nữ" : "Nam"; changed = true; }
                    if (string.IsNullOrWhiteSpace(u.CitizenId)) { u.CitizenId = $"07909600{1000 + idx}"; changed = true; }
                    if (string.IsNullOrWhiteSpace(u.Address)) { u.Address = $"Số {15 + idx * 8} Điện Biên Phủ, TP. Hồ Chí Minh"; changed = true; }
                    if (!u.HireDate.HasValue) { u.HireDate = new DateTime(2023, (idx % 12) + 1, 1); changed = true; }
                    if (string.IsNullOrWhiteSpace(u.PhoneNumber)) { u.PhoneNumber = $"09081234{10 + idx:D2}"; changed = true; }
                    if (string.IsNullOrWhiteSpace(u.Position)) { u.Position = "Nhân Viên Chính Thức"; changed = true; }
                }

                if (string.IsNullOrWhiteSpace(u.AssignedShift))
                {
                    u.AssignedShift = WorkShifts.HanhChinh;
                    changed = true;
                }

                if (changed)
                {
                    await userManager.UpdateAsync(u);
                }

                // Đảm bảo có cấu hình lương theo chức vụ
                var hasConfig = await context.SalaryConfigs.AnyAsync(s => s.UserId == u.Id);
                if (!hasConfig)
                {
                    var stdRate = sampleProfiles.TryGetValue(uname, out var sp)
                        ? sp.Rate
                        : SalaryCalculator.ResolveStandardHourlyRateByPosition(u.Position);

                    context.SalaryConfigs.Add(new SalaryConfig
                    {
                        UserId = u.Id,
                        HourlyRate = stdRate,
                        OvertimeRateMultiplier = 1.5m,
                        StandardDailyHours = 8.0,
                        EffectiveDate = new DateTime(2024, 1, 1),
                        Note = $"Định mức lương chuẩn theo chức vụ ({u.Position})",
                        CreatedAt = now
                    });
                    await context.SaveChangesAsync();
                }

                idx++;
            }
        }
    }
}
