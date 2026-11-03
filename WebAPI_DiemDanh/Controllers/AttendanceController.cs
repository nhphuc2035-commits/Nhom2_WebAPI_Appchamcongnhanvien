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
    public class AttendanceController : ControllerBase
    {
        private readonly IAttendanceRepository _attendanceRepository;

        public AttendanceController(IAttendanceRepository attendanceRepository)
        {
            _attendanceRepository = attendanceRepository;
        }

        [Authorize]
        [HttpGet("today")]
        public async Task<IActionResult> GetTodayStatus()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<TodayAttendanceStatusModel>.Fail("Không xác định được danh tính nhân viên."));

            var result = await _attendanceRepository.GetTodayStatusAsync(userId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize]
        [HttpPost("check-in")]
        public async Task<IActionResult> CheckIn([FromBody] CheckInRequestModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? model.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<AttendanceRecordResponseModel>.Fail("Không xác định được danh tính nhân viên."));

            var result = await _attendanceRepository.CheckInAsync(userId, model);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize]
        [HttpPost("check-out")]
        public async Task<IActionResult> CheckOut([FromBody] CheckOutRequestModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? model.UserId;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<AttendanceRecordResponseModel>.Fail("Không xác định được danh tính nhân viên."));

            var result = await _attendanceRepository.CheckOutAsync(userId, model);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Quản lý hoặc Admin chấm công thủ công cho nhân viên
        /// </summary>
        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpPost("manual")]
        public async Task<IActionResult> ManualAttendance([FromBody] ManualAttendanceModel model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(ApiResponse<AttendanceRecordResponseModel>.Fail(errors.FirstOrDefault() ?? "Dữ liệu chấm công thủ công không hợp lệ.", errors));
            }

            var managerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Admin";
            var result = await _attendanceRepository.ManualAttendanceAsync(model, managerUserId);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize]
        [HttpGet("my-history")]
        public async Task<IActionResult> GetMyHistory([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<List<AttendanceRecordResponseModel>>.Fail("Không tìm thấy thông tin xác thực người dùng."));

            var result = await _attendanceRepository.GetUserHistoryAsync(userId, fromDate, toDate);
            return Ok(result);
        }

        /// <summary>
        /// Báo cáo Tổng Kết Tháng phong cách Spotify (Monthly Work Wrapped) cho nhân viên
        /// </summary>
        [Authorize]
        [HttpGet("monthly-wrapped")]
        public async Task<IActionResult> GetMonthlyWrapped([FromQuery] int? year, [FromQuery] int? month)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<MonthlyWrappedResponseModel>.Fail("Không tìm thấy thông tin xác thực người dùng."));

            var now = VnTime.Now;
            var targetYear = year ?? now.Year;
            var targetMonth = month ?? now.Month;

            var result = await _attendanceRepository.GetMonthlyWrappedAsync(userId, targetYear, targetMonth);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("all")]
        public async Task<IActionResult> GetAllAttendance(
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] string? department,
            [FromQuery] string? userId,
            [FromQuery] string? status,
            [FromQuery] string? search)
        {
            var result = await _attendanceRepository.GetAllAsync(fromDate, toDate, department, userId, status, search);
            return Ok(result);
        }

        [Authorize]
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _attendanceRepository.GetByIdAsync(id);
            return result.Success ? Ok(result) : NotFound(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var operatorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isOperatorAdmin = User.IsInRole(UserRoles.Admin);
            var result = await _attendanceRepository.DeleteAsync(id, operatorUserId, isOperatorAdmin);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}
