using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WebAPI_DiemDanh.Helpers;
using WebAPI_DiemDanh.Models;
using WebAPI_DiemDanh.Repositories;

namespace WebAPI_DiemDanh.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SalaryController : ControllerBase
    {
        private readonly ISalaryRepository _salaryRepository;

        public SalaryController(ISalaryRepository salaryRepository)
        {
            _salaryRepository = salaryRepository;
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("calculate")]
        public async Task<IActionResult> CalculateSalaries([FromQuery] CalculateSalaryFilterModel filter)
        {
            var result = await _salaryRepository.CalculateAllSalariesAsync(filter);
            return Ok(result);
        }

        [Authorize]
        [HttpGet("my-salary")]
        public async Task<IActionResult> GetMySalary(
            [FromQuery] string? periodType,
            [FromQuery] int? year,
            [FromQuery] int? month,
            [FromQuery] int? weekNumber,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] string? periodDescription)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<SalaryCalculationResultModel>.Fail("Không xác định được danh tính người dùng."));

            var range = PeriodHelper.Resolve(periodType, year, month, null, weekNumber, fromDate, toDate);
            var desc = string.IsNullOrWhiteSpace(periodDescription) ? range.Label : periodDescription;

            var result = await _salaryRepository.CalculateEmployeeSalaryAsync(userId, range.From, range.To, desc, range.PeriodType);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.Admin)]
        [HttpPost("config")]
        public async Task<IActionResult> SetSalaryConfig([FromBody] SalaryConfigModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<SalaryConfig>.Fail("Dữ liệu cấu hình lương không hợp lệ."));

            var result = await _salaryRepository.SetSalaryConfigAsync(model);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("config/{userId}")]
        public async Task<IActionResult> GetSalaryConfig(string userId)
        {
            var result = await _salaryRepository.GetSalaryConfigAsync(userId);
            return Ok(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("config/{userId}/history")]
        public async Task<IActionResult> GetSalaryConfigHistory(string userId)
        {
            var result = await _salaryRepository.GetSalaryConfigHistoryAsync(userId);
            return Ok(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("position-rates")]
        public async Task<IActionResult> GetPositionRates()
        {
            var result = await _salaryRepository.GetPositionRatesAsync();
            return Ok(result);
        }

        [Authorize(Roles = UserRoles.Admin)]
        [HttpPut("position-rates")]
        public async Task<IActionResult> UpdatePositionRate([FromBody] UpdatePositionRateModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<PositionRate>.Fail("Dữ liệu định mức lương chức vụ không hợp lệ."));

            var result = await _salaryRepository.UpdatePositionRateAsync(model);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.Admin)]
        [HttpPost("bonus")]
        public async Task<IActionResult> SetSalaryBonus([FromBody] SetSalaryBonusModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<SalaryBonus>.Fail("Dữ liệu tiền thưởng không hợp lệ."));

            var result = await _salaryRepository.SetSalaryBonusAsync(model);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("bonus")]
        public async Task<IActionResult> GetSalaryBonuses([FromQuery] int? year, [FromQuery] int? month, [FromQuery] string? userId)
        {
            var result = await _salaryRepository.GetSalaryBonusesAsync(year, month, userId);
            return Ok(result);
        }

        [Authorize(Roles = UserRoles.Admin)]
        [HttpDelete("bonus/{id:int}")]
        public async Task<IActionResult> DeleteSalaryBonus(int id)
        {
            var result = await _salaryRepository.DeleteSalaryBonusAsync(id);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}
