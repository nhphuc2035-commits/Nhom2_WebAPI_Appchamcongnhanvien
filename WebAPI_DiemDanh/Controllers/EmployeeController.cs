using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WebAPI_DiemDanh.Models;
using WebAPI_DiemDanh.Repositories;

namespace WebAPI_DiemDanh.Controllers
{
    [Authorize(Roles = UserRoles.AdminOrManager)]
    [Route("api/[controller]")]
    [ApiController]
    public class EmployeeController : ControllerBase
    {
        private readonly IEmployeeRepository _employeeRepository;

        public EmployeeController(IEmployeeRepository employeeRepository)
        {
            _employeeRepository = employeeRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] string? searchTerm,
            [FromQuery] string? department,
            [FromQuery] bool? isActive,
            [FromQuery] string? role)
        {
            var result = await _employeeRepository.GetAllEmployeesAsync(searchTerm, department, isActive, role);
            return Ok(result);
        }

        [HttpGet("departments")]
        public async Task<IActionResult> GetDepartments()
        {
            var result = await _employeeRepository.GetDepartmentsAsync();
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var result = await _employeeRepository.GetEmployeeByIdAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [Authorize(Roles = UserRoles.Admin)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] RegistrationModel model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(ApiResponse<UserDto>.Fail(errors.FirstOrDefault() ?? "Dữ liệu nhân viên không hợp lệ.", errors));
            }

            var result = await _employeeRepository.CreateEmployeeAsync(model);
            return result.Success ? CreatedAtAction(nameof(GetById), new { id = result.Data?.Id }, result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.Admin)]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateUserModel model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(ApiResponse<UserDto>.Fail(errors.FirstOrDefault() ?? "Dữ liệu cập nhật không hợp lệ.", errors));
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var result = await _employeeRepository.UpdateEmployeeAsync(id, model, currentUserId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.Admin)]
        [HttpPatch("{id}/status")]
        public async Task<IActionResult> ToggleStatus(string id, [FromBody] UserStatusUpdateModel model)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var result = await _employeeRepository.ToggleEmployeeStatusAsync(id, model.IsActive, currentUserId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.Admin)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var result = await _employeeRepository.DeleteEmployeeAsync(id, currentUserId);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}
