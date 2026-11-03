using System.ComponentModel.DataAnnotations;

namespace WebAPI_DiemDanh.Models
{
    public static class ReportPeriodTypes
    {
        public const string Week = "Week";
        public const string Month = "Month";
        public const string Quarter = "Quarter";
        public const string Year = "Year";
    }

    public class ReportFilterModel
    {
        /// <summary>"Week" | "Month" | "Quarter" | "Year"</summary>
        public string PeriodType { get; set; } = ReportPeriodTypes.Month;

        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Quarter { get; set; }
        public int? WeekNumber { get; set; }

        public string? Department { get; set; }
        public string? UserId { get; set; }

        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class PeriodicAttendanceSummary
    {
        public string PeriodKey { get; set; } = string.Empty;
        public string PeriodLabel { get; set; } = string.Empty;
        public double TotalHours { get; set; }
        public int TotalCheckIns { get; set; }
        public int TotalManualEntries { get; set; }
        public int ActiveEmployeesCount { get; set; }
        public double AverageHoursPerEmployee { get; set; }
    }

    public class DepartmentAttendanceSummary
    {
        public string Department { get; set; } = "Chưa phân ban";
        public int TotalEmployees { get; set; }
        public double TotalHoursWorked { get; set; }
        public double AverageHoursPerEmployee { get; set; }
        public int TotalCheckIns { get; set; }
    }

    public class EmployeeDailyAttendance
    {
        public DateTime Date { get; set; }
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public double HoursWorked { get; set; }
        public string AuthenticationMethod { get; set; } = string.Empty;
        public bool IsManual { get; set; }
        public string? Remarks { get; set; }
    }

    public class EmployeeAttendanceReportModel
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? Position { get; set; }
        public string PeriodType { get; set; } = ReportPeriodTypes.Month;
        public string PeriodLabel { get; set; } = string.Empty;
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int TotalDaysPresent { get; set; }
        public double TotalHoursWorked { get; set; }
        public double RegularHours { get; set; }
        public double OvertimeHours { get; set; }
        public int TotalManualEntries { get; set; }
        public int MissingCheckouts { get; set; }
        public double AverageHoursPerDay { get; set; }
        public decimal? EstimatedSalary { get; set; }
        public decimal? HourlyRate { get; set; }
        public List<EmployeeDailyAttendance> DailyRecords { get; set; } = new();
    }

    /// <summary>Tổng hợp theo từng nhân viên trong kỳ báo cáo.</summary>
    public class EmployeePeriodSummary
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? Position { get; set; }
        public int DaysPresent { get; set; }
        public double TotalHours { get; set; }
        public double RegularHours { get; set; }
        public double OvertimeHours { get; set; }
        public int ManualEntries { get; set; }
        public int MissingCheckouts { get; set; }
        public decimal EstimatedSalary { get; set; }
    }

    public class ReportOverviewModel
    {
        public string PeriodType { get; set; } = ReportPeriodTypes.Month;
        public string PeriodLabel { get; set; } = string.Empty;
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int TotalEmployees { get; set; }
        public int EmployeesWithAttendance { get; set; }
        public int TotalDaysPresent { get; set; }
        public double TotalHours { get; set; }
        public double TotalOvertimeHours { get; set; }
        public int TotalManualEntries { get; set; }
        public int TotalMissingCheckouts { get; set; }
        public double AverageHoursPerEmployee { get; set; }
        public decimal EstimatedPayroll { get; set; }
        public List<PeriodicAttendanceSummary> Timeline { get; set; } = new();
        public List<DepartmentAttendanceSummary> Departments { get; set; } = new();
        public List<EmployeePeriodSummary> Employees { get; set; } = new();
    }

    public class DashboardDayPoint
    {
        public DateTime Date { get; set; }
        public string Label { get; set; } = string.Empty;
        public int CheckIns { get; set; }
        public double Hours { get; set; }
    }

    public class TodayEmployeeStatus
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Department { get; set; }
        public string? Position { get; set; }
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public double Hours { get; set; }
        public bool IsManual { get; set; }
        public string Status { get; set; } = AttendanceStatuses.NotCheckedIn;
    }

    public class DashboardSummaryModel
    {
        public DateTime ServerTime { get; set; }
        public string MonthLabel { get; set; } = string.Empty;
        public int TotalEmployees { get; set; }
        public int ActiveEmployees { get; set; }
        public int LockedEmployees { get; set; }
        public int ManagerCount { get; set; }
        public int BiometricEnrolledCount { get; set; }
        public int CheckedInToday { get; set; }
        public int CurrentlyWorking { get; set; }
        public int CompletedToday { get; set; }
        public int NotCheckedInToday { get; set; }
        public int MissingCheckouts { get; set; }
        public int ManualEntriesThisMonth { get; set; }
        public double HoursThisMonth { get; set; }
        public double OvertimeHoursThisMonth { get; set; }
        public decimal PayrollThisMonth { get; set; }
        public List<DashboardDayPoint> Last14Days { get; set; } = new();
        public List<DepartmentAttendanceSummary> Departments { get; set; } = new();
        public List<TodayEmployeeStatus> TodayAttendance { get; set; } = new();
        public List<AttendanceRecordResponseModel> RecentActivities { get; set; } = new();
    }

    public class SendEmailReportRequestModel
    {
        [Required(ErrorMessage = "Mã nhân viên là bắt buộc")]
        public string UserId { get; set; } = string.Empty;

        /// <summary>"Week" | "Month" | "Quarter" | "Year"</summary>
        public string PeriodType { get; set; } = ReportPeriodTypes.Month;

        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Quarter { get; set; }
        public int? WeekNumber { get; set; }
    }

    public class SendBulkEmailRequestModel
    {
        public string PeriodType { get; set; } = ReportPeriodTypes.Month;
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Quarter { get; set; }
        public int? WeekNumber { get; set; }
        public string? Department { get; set; }
    }

    public class EmailSendResult
    {
        public string ToEmail { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public bool Sent { get; set; }

        /// <summary>true = chưa cấu hình SMTP thật, email được lưu thành file HTML để xem trước.</summary>
        public bool Simulated { get; set; }

        public string? PreviewUrl { get; set; }
        public string? Error { get; set; }
    }

    public class BulkEmailResult
    {
        public int Total { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public bool Simulated { get; set; }
        public List<EmailSendResult> Items { get; set; } = new();
    }

    public class EmailStatusModel
    {
        public bool IsConfigured { get; set; }
        public string Mode { get; set; } = "Simulation";
        public string SmtpServer { get; set; } = string.Empty;
        public int SmtpPort { get; set; }
        public string SenderEmail { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool HasPassword { get; set; }
    }
}
