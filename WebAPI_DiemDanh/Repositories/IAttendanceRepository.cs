using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Repositories
{
    public interface IAttendanceRepository
    {
        Task<ApiResponse<AttendanceRecordResponseModel>> CheckInAsync(string userId, CheckInRequestModel model);
        Task<ApiResponse<AttendanceRecordResponseModel>> CheckOutAsync(string userId, CheckOutRequestModel model);
        Task<ApiResponse<TodayAttendanceStatusModel>> GetTodayStatusAsync(string userId);
        Task<ApiResponse<AttendanceRecordResponseModel>> ManualAttendanceAsync(ManualAttendanceModel model, string managerUserId);
        Task<ApiResponse<List<AttendanceRecordResponseModel>>> GetUserHistoryAsync(string userId, DateTime? fromDate, DateTime? toDate);
        Task<ApiResponse<List<AttendanceRecordResponseModel>>> GetAllAsync(DateTime? fromDate, DateTime? toDate, string? department, string? userId, string? status, string? search);
        Task<ApiResponse<MonthlyWrappedResponseModel>> GetMonthlyWrappedAsync(string userId, int year, int month);
        Task<ApiResponse<AttendanceRecordResponseModel>> GetByIdAsync(int id);
        Task<ApiResponse<bool>> DeleteAsync(int id, string? operatorUserId = null, bool isOperatorAdmin = false);
    }
}
