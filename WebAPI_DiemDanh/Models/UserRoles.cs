namespace WebAPI_DiemDanh.Models
{
    public static class UserRoles
    {
        /// <summary>Quản trị viên: toàn quyền (tài khoản, lương, cấu hình).</summary>
        public const string Admin = "Admin";

        /// <summary>Quản lý: xem nhân sự, giám sát &amp; chấm công thủ công, báo cáo, bảng lương.</summary>
        public const string Manager = "Manager";

        /// <summary>Nhân viên: chỉ dùng App di động để điểm danh.</summary>
        public const string User = "User";

        /// <summary>Dùng cho [Authorize(Roles = ...)] — Admin hoặc Quản lý.</summary>
        public const string AdminOrManager = Admin + "," + Manager;

        public static readonly string[] All = { Admin, Manager, User };

        public static string Normalize(string? role)
        {
            if (string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase)) return Admin;
            if (string.Equals(role, Manager, StringComparison.OrdinalIgnoreCase)) return Manager;
            return User;
        }
    }
}
