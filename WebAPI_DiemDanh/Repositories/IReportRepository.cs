using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Repositories
{
    public interface IReportRepository
    {
        Task<ApiResponse<DashboardSummaryModel>> GetDashboardAsync();
        Task<ApiResponse<ReportOverviewModel>> GetOverviewAsync(ReportFilterModel filter);
        Task<ApiResponse<List<PeriodicAttendanceSummary>>> GetStatisticsReportAsync(ReportFilterModel filter);
        Task<ApiResponse<List<DepartmentAttendanceSummary>>> GetDepartmentStatisticsAsync(DateTime fromDate, DateTime toDate);
        Task<ApiResponse<EmployeeAttendanceReportModel>> GetEmployeePersonalReportAsync(string userId, DateTime fromDate, DateTime toDate, string? periodLabel = null, string periodType = "Custom");
    }
}
