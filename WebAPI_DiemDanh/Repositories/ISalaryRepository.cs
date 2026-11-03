using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Repositories
{
    public interface ISalaryRepository
    {
        Task<ApiResponse<SalaryCalculationResultModel>> CalculateEmployeeSalaryAsync(string userId, DateTime fromDate, DateTime toDate, string? periodDescription = null, string periodType = "Custom");
        Task<ApiResponse<SalaryPayrollSummaryModel>> CalculateAllSalariesAsync(CalculateSalaryFilterModel filter);
        Task<ApiResponse<SalaryConfig>> SetSalaryConfigAsync(SalaryConfigModel model);
        Task<ApiResponse<SalaryConfig>> GetSalaryConfigAsync(string userId);
        Task<ApiResponse<List<SalaryConfig>>> GetSalaryConfigHistoryAsync(string userId);
        Task<ApiResponse<List<PositionRate>>> GetPositionRatesAsync();
        Task<ApiResponse<PositionRate>> UpdatePositionRateAsync(UpdatePositionRateModel model);
        Task<ApiResponse<SalaryBonus>> SetSalaryBonusAsync(SetSalaryBonusModel model);
        Task<ApiResponse<List<SalaryBonus>>> GetSalaryBonusesAsync(int? year, int? month, string? userId);
        Task<ApiResponse<bool>> DeleteSalaryBonusAsync(int id);
    }
}
