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
    public class ReportController : ControllerBase
    {
        private readonly IReportRepository _reportRepository;
        private readonly IEmailRepository _emailRepository;

        public ReportController(IReportRepository reportRepository, IEmailRepository emailRepository)
        {
            _reportRepository = reportRepository;
            _emailRepository = emailRepository;
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var result = await _reportRepository.GetDashboardAsync();
            return Ok(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("overview")]
        public async Task<IActionResult> GetOverview([FromQuery] ReportFilterModel filter)
        {
            var result = await _reportRepository.GetOverviewAsync(filter);
            return Ok(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("statistics")]
        public async Task<IActionResult> GetStatistics([FromQuery] ReportFilterModel filter)
        {
            var result = await _reportRepository.GetStatisticsReportAsync(filter);
            return Ok(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("department")]
        public async Task<IActionResult> GetDepartmentStatistics([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
        {
            var now = VnTime.Today;
            var from = fromDate ?? new DateTime(now.Year, now.Month, 1);
            var to = toDate ?? now;
            var result = await _reportRepository.GetDepartmentStatisticsAsync(from, to);
            return Ok(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("employee/{userId}")]
        public async Task<IActionResult> GetEmployeeReport(
            string userId,
            [FromQuery] string? periodType,
            [FromQuery] int? year,
            [FromQuery] int? month,
            [FromQuery] int? quarter,
            [FromQuery] int? weekNumber,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            var range = PeriodHelper.Resolve(periodType, year, month, quarter, weekNumber, fromDate, toDate);
            var result = await _reportRepository.GetEmployeePersonalReportAsync(userId, range.From, range.To, range.Label, range.PeriodType);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize]
        [HttpGet("my-report")]
        public async Task<IActionResult> GetMyReport(
            [FromQuery] string? periodType,
            [FromQuery] int? year,
            [FromQuery] int? month,
            [FromQuery] int? quarter,
            [FromQuery] int? weekNumber,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<EmployeeAttendanceReportModel>.Fail("Không xác định được danh tính người dùng."));

            var range = PeriodHelper.Resolve(periodType, year, month, quarter, weekNumber, fromDate, toDate);
            var result = await _reportRepository.GetEmployeePersonalReportAsync(userId, range.From, range.To, range.Label, range.PeriodType);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpGet("email-status")]
        public IActionResult GetEmailStatus()
        {
            return Ok(ApiResponse<EmailStatusModel>.Ok(_emailRepository.GetEmailStatus(), "Trạng thái cấu hình Email."));
        }

        [Authorize(Roles = UserRoles.Admin)]
        [HttpPost("email-settings")]
        public IActionResult UpdateEmailSettings([FromBody] UpdateEmailSettingsModel model)
        {
            _emailRepository.UpdateRuntimeSettings(model);
            return Ok(ApiResponse<EmailStatusModel>.Ok(_emailRepository.GetEmailStatus(), "Đã cập nhật cấu hình gửi Email."));
        }

        [Authorize]
        [HttpPost("preview-email")]
        public async Task<IActionResult> PreviewEmail([FromBody] SendEmailReportRequestModel request)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdminOrMgr = User.IsInRole(UserRoles.Admin) || User.IsInRole(UserRoles.Manager);

            if (!isAdminOrMgr || string.IsNullOrWhiteSpace(request.UserId))
            {
                request.UserId = currentUserId ?? string.Empty;
            }

            var result = await _emailRepository.GenerateReportEmailPreviewHtmlAsync(request);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpPost("send-email")]
        public async Task<IActionResult> SendReportEmail([FromBody] SendEmailReportRequestModel request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<EmailSendResult>.Fail("Dữ liệu gửi email không hợp lệ."));

            var result = await _emailRepository.SendAttendanceReportEmailAsync(request);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize(Roles = UserRoles.AdminOrManager)]
        [HttpPost("send-bulk-email")]
        public async Task<IActionResult> SendBulkReportEmails([FromBody] SendBulkEmailRequestModel request)
        {
            var result = await _emailRepository.SendBulkAttendanceReportEmailsAsync(request);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Authorize]
        [HttpPost("send-my-report")]
        public async Task<IActionResult> SendMyReportEmail(
            [FromQuery] string periodType = "Month",
            [FromQuery] int? year = null,
            [FromQuery] int? month = null,
            [FromQuery] int? quarter = null,
            [FromQuery] int? weekNumber = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(ApiResponse<EmailSendResult>.Fail("Không xác định được danh tính người dùng."));

            var request = new SendEmailReportRequestModel
            {
                UserId = userId,
                PeriodType = periodType,
                Year = year,
                Month = month,
                Quarter = quarter,
                WeekNumber = weekNumber
            };

            var result = await _emailRepository.SendAttendanceReportEmailAsync(request);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}
