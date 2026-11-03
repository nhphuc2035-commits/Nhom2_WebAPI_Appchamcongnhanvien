using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Repositories
{
    public interface IEmailRepository
    {
        EmailStatusModel GetEmailStatus();
        void UpdateRuntimeSettings(UpdateEmailSettingsModel model);
        Task<ApiResponse<string>> GenerateReportEmailPreviewHtmlAsync(SendEmailReportRequestModel request);
        Task<ApiResponse<EmailSendResult>> SendAttendanceReportEmailAsync(SendEmailReportRequestModel request);
        Task<ApiResponse<BulkEmailResult>> SendBulkAttendanceReportEmailsAsync(SendBulkEmailRequestModel request);
    }
}
