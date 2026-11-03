namespace WebAPI_DiemDanh.Helpers
{
    /// <summary>
    /// Thời gian chuẩn Việt Nam (UTC+7). Toàn bộ nghiệp vụ chấm công dùng giờ do SERVER cấp,
    /// không tin tưởng giờ gửi từ điện thoại (chống chỉnh giờ máy để gian lận).
    /// </summary>
    public static class VnTime
    {
        private static readonly TimeZoneInfo Zone = ResolveZone();

        private static TimeZoneInfo ResolveZone()
        {
            foreach (var id in new[] { "SE Asia Standard Time", "Asia/Ho_Chi_Minh", "Asia/Bangkok" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch { /* thử id tiếp theo */ }
            }
            return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "Việt Nam", "Việt Nam");
        }

        public static DateTime Now =>
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone), DateTimeKind.Unspecified);

        public static DateTime Today => Now.Date;
    }
}
