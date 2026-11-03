using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebAPI_DiemDanh.Models
{
    public class CalculateSalaryFilterModel
    {
        public string? UserId { get; set; }
        public string? Department { get; set; }

        /// <summary>"Week" | "Month" | "Custom"</summary>
        public string PeriodType { get; set; } = "Month";

        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? WeekNumber { get; set; }

        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class SalaryDailyItem
    {
        public DateTime Date { get; set; }
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public double Hours { get; set; }
        public double RegularHours { get; set; }
        public double OvertimeHours { get; set; }
        public decimal Amount { get; set; }
        public bool IsManual { get; set; }
        public bool IsWeekend { get; set; }
        public decimal DayMultiplier { get; set; } = 1.0m;
    }

    public class SalaryCalculationResultModel
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? Position { get; set; }
        public string PeriodType { get; set; } = "Month";
        public string PeriodDescription { get; set; } = string.Empty;
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int StandardWorkingDays { get; set; }
        public int WorkingDaysCount { get; set; }
        public double TotalWorkingHours { get; set; }
        public double RegularHours { get; set; }
        public double OvertimeHours { get; set; }
        public double StandardDailyHours { get; set; }
        public decimal HourlyRate { get; set; }
        public decimal OvertimeMultiplier { get; set; }
        public decimal RegularSalary { get; set; }
        public decimal OvertimeSalary { get; set; }
        public decimal BonusSalary { get; set; }
        public string? BonusReason { get; set; }
        public decimal TotalSalary { get; set; }

        /// <summary>Số ca chưa check-out (không được tính lương cho tới khi quản lý chấm bù).</summary>
        public int IncompleteRecords { get; set; }

        public DateTime? HireDate { get; set; }
        public string? HireNote { get; set; }

        public List<SalaryDailyItem> DailyBreakdown { get; set; } = new();
        public DateTime CalculatedAt { get; set; }
    }

    public class SalaryPayrollSummaryModel
    {
        public string PeriodType { get; set; } = "Month";
        public string PeriodDescription { get; set; } = string.Empty;
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int EmployeeCount { get; set; }
        public double TotalHours { get; set; }
        public double TotalRegularHours { get; set; }
        public double TotalOvertimeHours { get; set; }
        public decimal TotalRegularSalary { get; set; }
        public decimal TotalOvertimeSalary { get; set; }
        public decimal TotalBonusSalary { get; set; }
        public decimal TotalPayroll { get; set; }
        public List<SalaryCalculationResultModel> Items { get; set; } = new();
    }

    public class SalaryConfigModel
    {
        [Required(ErrorMessage = "Mã nhân viên là bắt buộc")]
        public string UserId { get; set; } = string.Empty;

        [Range(1000, 100000000, ErrorMessage = "Mức lương giờ phải lớn hơn 1,000 VND")]
        public decimal HourlyRate { get; set; } = 50000m;

        [Range(1.0, 5.0, ErrorMessage = "Hệ số tăng ca từ 1.0 đến 5.0")]
        public decimal OvertimeRateMultiplier { get; set; } = 1.5m;

        [Range(1.0, 24.0, ErrorMessage = "Giờ làm tiêu chuẩn từ 1 đến 24 giờ")]
        public double StandardDailyHours { get; set; } = 8.0;

        public string? Note { get; set; }
    }

    public class PositionRate
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string PositionName { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal HourlyRate { get; set; } = 50000m;

        [MaxLength(50)]
        public string Role { get; set; } = "User";

        [Column(TypeName = "decimal(5,2)")]
        public decimal OvertimeMultiplier { get; set; } = 1.5m;

        public double StandardDailyHours { get; set; } = 8.0;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SalaryBonus
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public int Year { get; set; }
        public int Month { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; } = 0m;

        [MaxLength(255)]
        public string? Reason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpdatePositionRateModel
    {
        [Required]
        public string PositionName { get; set; } = string.Empty;

        [Range(1000, 100000000, ErrorMessage = "Mức lương giờ phải từ 1.000 đến 100.000.000")]
        public decimal HourlyRate { get; set; }

        public string? Role { get; set; }
        public decimal? OvertimeMultiplier { get; set; }
        public double? StandardDailyHours { get; set; }

        /// <summary>Có tự động áp dụng cập nhật lương cho tất cả nhân viên đang giữ chức vụ này không?</summary>
        public bool ApplyToCurrentEmployees { get; set; } = true;
    }

    public class SetSalaryBonusModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        public int Year { get; set; }
        public int Month { get; set; }

        [Range(0, 1000000000, ErrorMessage = "Tiền thưởng từ 0 đến 1 tỷ VNĐ")]
        public decimal Amount { get; set; }

        [MaxLength(255)]
        public string? Reason { get; set; }
    }
}
