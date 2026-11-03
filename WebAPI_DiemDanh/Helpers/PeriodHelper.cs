using System.Globalization;

namespace WebAPI_DiemDanh.Helpers
{
    public record PeriodRange(DateTime From, DateTime To, string Label, string PeriodType);

    /// <summary>
    /// Quy đổi kỳ thống kê (Tuần / Tháng / Quý / Năm / Tùy chọn) thành khoảng ngày cụ thể.
    /// Tuần tính theo chuẩn ISO-8601 (Thứ Hai -> Chủ Nhật).
    /// </summary>
    public static class PeriodHelper
    {
        public const string Week = "Week";
        public const string Month = "Month";
        public const string Quarter = "Quarter";
        public const string Year = "Year";
        public const string Custom = "Custom";

        public static string Normalize(string? periodType)
        {
            return (periodType ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "week" or "tuan" or "tuần" => Week,
                "quarter" or "quy" or "quý" => Quarter,
                "year" or "nam" or "năm" => Year,
                "custom" => Custom,
                _ => Month
            };
        }

        public static PeriodRange Resolve(string? periodType, int? year, int? month, int? quarter, int? week,
            DateTime? fromDate = null, DateTime? toDate = null)
        {
            var now = VnTime.Now;
            var type = Normalize(periodType);

            if (fromDate.HasValue && toDate.HasValue)
            {
                var f = fromDate.Value.Date;
                var t = toDate.Value.Date;
                if (t < f) (f, t) = (t, f);
                return new PeriodRange(f, t, $"{f:dd/MM/yyyy} - {t:dd/MM/yyyy}", Custom);
            }

            var y = year is > 2000 and < 2100 ? year.Value : now.Year;

            switch (type)
            {
                case Week:
                {
                    int isoYear;
                    int w;
                    if (week is >= 1 and <= 53)
                    {
                        isoYear = y;
                        w = Math.Min(week.Value, ISOWeek.GetWeeksInYear(isoYear));
                    }
                    else
                    {
                        isoYear = ISOWeek.GetYear(now);
                        w = ISOWeek.GetWeekOfYear(now);
                    }
                    var from = ISOWeek.ToDateTime(isoYear, w, DayOfWeek.Monday);
                    var to = from.AddDays(6);
                    return new PeriodRange(from, to, $"Tuần {w}/{isoYear} ({from:dd/MM} - {to:dd/MM})", Week);
                }
                case Quarter:
                {
                    var q = quarter is >= 1 and <= 4 ? quarter.Value : (now.Month - 1) / 3 + 1;
                    var from = new DateTime(y, (q - 1) * 3 + 1, 1);
                    var to = from.AddMonths(3).AddDays(-1);
                    return new PeriodRange(from, to, $"Quý {q}/{y}", Quarter);
                }
                case Year:
                {
                    return new PeriodRange(new DateTime(y, 1, 1), new DateTime(y, 12, 31), $"Năm {y}", Year);
                }
                default:
                {
                    var m = month is >= 1 and <= 12 ? month.Value : now.Month;
                    var from = new DateTime(y, m, 1);
                    var to = from.AddMonths(1).AddDays(-1);
                    return new PeriodRange(from, to, $"Tháng {m:D2}/{y}", Month);
                }
            }
        }
    }
}
