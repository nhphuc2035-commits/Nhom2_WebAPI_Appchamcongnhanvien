using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Repositories
{
    public interface IEmployeeRepository
    {
        Task<ApiResponse<List<UserDto>>> GetAllEmployeesAsync(string? searchTerm, string? department, bool? isActive, string? role);
        Task<ApiResponse<UserDto>> GetEmployeeByIdAsync(string id);
        Task<ApiResponse<List<string>>> GetDepartmentsAsync();
        Task<ApiResponse<UserDto>> CreateEmployeeAsync(RegistrationModel model);
        Task<ApiResponse<UserDto>> UpdateEmployeeAsync(string id, UpdateUserModel model, string currentUserId);
        Task<ApiResponse<bool>> ToggleEmployeeStatusAsync(string id, bool isActive, string currentUserId);
        Task<ApiResponse<bool>> DeleteEmployeeAsync(string id, string currentUserId);
    }
}
