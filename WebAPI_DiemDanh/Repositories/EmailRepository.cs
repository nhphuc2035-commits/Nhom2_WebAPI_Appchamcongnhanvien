using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Text;
using System.Text.Json;
using WebAPI_DiemDanh.Helpers;
using WebAPI_DiemDanh.Models;

namespace WebAPI_DiemDanh.Repositories
{
    public class EmailRepository : IEmailRepository
    {
        private static EmailSettings? _runtimeOverride;

        private readonly EmailSettings _defaultSettings;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IReportRepository _reportRepository;
        private readonly ISalaryRepository _salaryRepository;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EmailRepository> _logger;
        private static readonly string SettingsFilePath = Path.Combine(AppContext.BaseDirectory, "runtime-email-settings.json");

        public EmailRepository(
            IOptions<EmailSettings> emailSettings,
            UserManager<ApplicationUser> userManager,
            IReportRepository reportRepository,
            ISalaryRepository salaryRepository,
            IWebHostEnvironment env,
            ILogger<EmailRepository> logger)
        {
            _defaultSettings = emailSettings.Value;
            _userManager = userManager;
            _reportRepository = reportRepository;
            _salaryRepository = salaryRepository;
            _env = env;
            _logger = logger;

            LoadPersistedSettings();
        }

        private void LoadPersistedSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var saved = JsonSerializer.Deserialize<EmailSettings>(json);
                    if (saved != null && !string.IsNullOrWhiteSpace(saved.SenderEmail))
                    {
                        _runtimeOverride = saved;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Không thể tải runtime-email-settings.json: {Message}", ex.Message);
            }
        }

        private EmailSettings CurrentSettings => _runtimeOverride ?? _defaultSettings;

        private bool IsRealSmtpConfigured()
        {
            var s = CurrentSettings;
            if (string.IsNullOrWhiteSpace(s.SmtpServer) || string.IsNullOrWhiteSpace(s.Username) || string.IsNullOrWhiteSpace(s.Password))
                return false;

            var pwd = s.Password.Trim();
            if (pwd.Equals("your-app-password-here", StringComparison.OrdinalIgnoreCase) ||
                pwd.Equals("your_app_password", StringComparison.OrdinalIgnoreCase) ||
                pwd.Length < 8)
                return false;

            return true;
        }

        public EmailStatusModel GetEmailStatus()
        {
            var s = CurrentSettings;
            var configured = IsRealSmtpConfigured();
            return new EmailStatusModel
            {
                IsConfigured = configured,
                Mode = configured ? "SMTP" : "Simulation",
                SmtpServer = s.SmtpServer,
                SmtpPort = s.SmtpPort,
                SenderEmail = s.SenderEmail,
                SenderName = s.SenderName,
                Username = s.Username,
                HasPassword = !string.IsNullOrWhiteSpace(s.Password)
            };
        }

        public void UpdateRuntimeSettings(UpdateEmailSettingsModel model)
        {
            var existingPwd = _runtimeOverride?.Password ?? _defaultSettings.Password ?? string.Empty;
            var pwd = string.IsNullOrWhiteSpace(model.Password) ? existingPwd : model.Password.Trim();

            var newSettings = new EmailSettings
            {
                SmtpServer = string.IsNullOrWhiteSpace(model.SmtpServer) ? "smtp.gmail.com" : model.SmtpServer.Trim(),
                SmtpPort = model.SmtpPort > 0 ? model.SmtpPort : 587,
                SenderName = string.IsNullOrWhiteSpace(model.SenderName) ? "Hệ thống Nhân sự & Chấm công" : model.SenderName.Trim(),
                SenderEmail = model.SenderEmail?.Trim() ?? string.Empty,
                Username = model.Username?.Trim() ?? string.Empty,
                Password = pwd,
                EnableSsl = model.EnableSsl
            };

            _runtimeOverride = newSettings;

            try
            {
                var json = JsonSerializer.Serialize(newSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Không thể lưu runtime-email-settings.json: {Message}", ex.Message);
            }
        }

        private async Task<string> SaveSimulatedEmailFileAsync(string userId, string htmlBody)
        {
            var webRoot = _env.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }

            var folder = Path.Combine(webRoot, "sent-emails");
            Directory.CreateDirectory(folder);

            var safeUser = new string(userId.Where(char.IsLetterOrDigit).Take(12).ToArray());
            var fileName = $"report_{safeUser}_{VnTime.Now:yyyyMMdd_HHmmss}.html";
            var fullPath = Path.Combine(folder, fileName);

            await File.WriteAllTextAsync(fullPath, htmlBody, Encoding.UTF8);
            return $"/sent-emails/{fileName}";
        }

        private async Task<(bool Sent, bool Simulated, string? PreviewUrl, string? Error)> DeliverEmailAsync(
            string userId, string toEmail, string subject, string htmlBody)
        {
            // Luôn lưu một bản HTML để Admin/Nhân viên có thể mở xem trực tiếp
            var previewUrl = await SaveSimulatedEmailFileAsync(userId, htmlBody);

            if (!IsRealSmtpConfigured())
            {
                _logger.LogInformation("Chế độ Mô phỏng Email: Đã xuất báo cáo HTML cho {Email} tại {Url}", toEmail, previewUrl);
                return (true, true, previewUrl, null);
            }

            var s = CurrentSettings;
            try
            {
                var message = new MimeMessage();
                var senderEmail = string.IsNullOrWhiteSpace(s.SenderEmail) ? s.Username : s.SenderEmail;
                message.From.Add(new MailboxAddress(s.SenderName, senderEmail));
                message.To.Add(new MailboxAddress("", toEmail));
                message.Subject = subject;

                var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
                message.Body = bodyBuilder.ToMessageBody();

                using var client = new SmtpClient();
                client.ServerCertificateValidationCallback = (_, _, _, _) => true;

                await client.ConnectAsync(s.SmtpServer, s.SmtpPort,
                    s.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);

                await client.AuthenticateAsync(s.Username, s.Password.Replace(" ", ""));
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation("Đã gửi email SMTP thật thành công tới {Email}", toEmail);
                return (true, false, previewUrl, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gửi SMTP thất bại tới {Email}, tự động chuyển sang lưu bản xem trước HTML.", toEmail);
                return (true, true, previewUrl, ex.Message);
            }
        }

        private async Task<(ApplicationUser? User, PeriodRange Period, EmployeeAttendanceReportModel? Report, SalaryCalculationResultModel? Salary, string? Error)>
            BuildReportPayloadAsync(SendEmailReportRequestModel request)
        {
            var user = await _userManager.FindByIdAsync(request.UserId);
            if (user == null)
                return (null, default!, null, null, "Không tìm thấy nhân viên.");

            if (string.IsNullOrWhiteSpace(user.Email))
                return (user, default!, null, null, "Nhân viên này chưa có địa chỉ Email.");

            var period = PeriodHelper.Resolve(request.PeriodType, request.Year, request.Month, request.Quarter, request.WeekNumber);

            var reportRes = await _reportRepository.GetEmployeePersonalReportAsync(user.Id, period.From, period.To, period.Label, period.PeriodType);
            if (!reportRes.Success || reportRes.Data == null)
                return (user, period, null, null, "Không thể lấy dữ liệu chấm công của nhân viên.");

            var salaryRes = await _salaryRepository.CalculateEmployeeSalaryAsync(user.Id, period.From, period.To, period.Label, period.PeriodType);
            return (user, period, reportRes.Data, salaryRes.Data, null);
        }

        public async Task<ApiResponse<string>> GenerateReportEmailPreviewHtmlAsync(SendEmailReportRequestModel request)
        {
            var (user, period, report, salary, error) = await BuildReportPayloadAsync(request);
            if (error != null || user == null || report == null)
                return ApiResponse<string>.Fail(error ?? "Không thể tạo bản xem trước.");

            var html = GenerateReportEmailHtml(report, salary, period.Label);
            return ApiResponse<string>.Ok(html, "Tạo nội dung email báo cáo thành công.");
        }

        public async Task<ApiResponse<EmailSendResult>> SendAttendanceReportEmailAsync(SendEmailReportRequestModel request)
        {
            var (user, period, report, salary, error) = await BuildReportPayloadAsync(request);
            if (error != null || user == null || report == null)
                return ApiResponse<EmailSendResult>.Fail(error ?? "Không thể gửi báo cáo.");

            var html = GenerateReportEmailHtml(report, salary, period.Label);
            var subject = $"[Báo Cáo Chấm Công & Lương] {user.FullName} — {period.Label}";

            var (sent, simulated, previewUrl, smtpErr) = await DeliverEmailAsync(user.Id, user.Email!, subject, html);

            var result = new EmailSendResult
            {
                ToEmail = user.Email!,
                FullName = user.FullName,
                Sent = sent,
                Simulated = simulated,
                PreviewUrl = previewUrl,
                Error = smtpErr
            };

            var msg = simulated
                ? $"Đã xuất & gửi báo cáo kỳ {period.Label} cho {user.FullName} ({user.Email}). Bạn có thể bấm Xem phiếu báo cáo ngay."
                : $"Đã gửi email báo cáo kỳ {period.Label} tới hộp thư {user.Email} của {user.FullName}.";

            return ApiResponse<EmailSendResult>.Ok(result, msg);
        }

        public async Task<ApiResponse<BulkEmailResult>> SendBulkAttendanceReportEmailsAsync(SendBulkEmailRequestModel request)
        {
            var adminIds = (await _userManager.GetUsersInRoleAsync(UserRoles.Admin)).Select(u => u.Id).ToHashSet();
            var query = _userManager.Users.AsNoTracking().Where(u => u.IsActive && u.Email != null && u.Email != "");

            if (!string.IsNullOrWhiteSpace(request.Department))
                query = query.Where(u => u.Department == request.Department);

            var users = (await query.OrderBy(u => u.FullName).ToListAsync())
                .Where(u => !adminIds.Contains(u.Id))
                .ToList();

            var bulk = new BulkEmailResult
            {
                Total = users.Count,
                Simulated = !IsRealSmtpConfigured()
            };

            foreach (var u in users)
            {
                var singleReq = new SendEmailReportRequestModel
                {
                    UserId = u.Id,
                    PeriodType = request.PeriodType,
                    Year = request.Year,
                    Month = request.Month,
                    Quarter = request.Quarter,
                    WeekNumber = request.WeekNumber
                };

                var res = await SendAttendanceReportEmailAsync(singleReq);
                if (res.Success && res.Data != null)
                {
                    bulk.SuccessCount++;
                    bulk.Items.Add(res.Data);
                }
                else
                {
                    bulk.FailedCount++;
                    bulk.Items.Add(new EmailSendResult
                    {
                        ToEmail = u.Email ?? string.Empty,
                        FullName = u.FullName,
                        Sent = false,
                        Error = res.Message
                    });
                }
            }

            return ApiResponse<BulkEmailResult>.Ok(bulk,
                $"Đã xử lý gửi báo cáo cho {bulk.SuccessCount}/{bulk.Total} nhân viên.");
        }

        private static string GenerateReportEmailHtml(
            EmployeeAttendanceReportModel report,
            SalaryCalculationResultModel? salary,
            string periodTitle)
        {
            var sb = new StringBuilder();
            var regHours = salary?.RegularHours ?? report.RegularHours;
            var otHours = salary?.OvertimeHours ?? report.OvertimeHours;
            var hourlyRate = salary?.HourlyRate ?? report.HourlyRate ?? 50000m;
            var otMul = salary?.OvertimeMultiplier ?? 1.5m;
            var regSalary = salary?.RegularSalary ?? 0m;
            var otSalary = salary?.OvertimeSalary ?? 0m;
            var bonusSalary = salary?.BonusSalary ?? 0m;
            var bonusReason = string.IsNullOrWhiteSpace(salary?.BonusReason) ? "Thưởng cống hiến & đóng góp đặc biệt" : salary.BonusReason;
            var totalSalary = salary?.TotalSalary ?? report.EstimatedSalary ?? 0m;

            sb.Append($@"<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='utf-8'/>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'/>
    <title>Báo Cáo Chấm Công &amp; Lương - {report.FullName}</title>
    <style>
        body {{ font-family: 'Segoe UI', Roboto, Arial, sans-serif; line-height: 1.6; color: #1e293b; margin: 0; padding: 24px; background-color: #f1f5f9; }}
        .container {{ max-width: 780px; margin: 0 auto; background: #ffffff; border-radius: 16px; overflow: hidden; box-shadow: 0 10px 30px rgba(15, 23, 42, 0.08); border: 1px solid #e2e8f0; }}
        .banner {{ background: linear-gradient(135deg, #0f172a 0%, #1e3a8a 60%, #2563eb 100%); color: #ffffff; padding: 28px 32px; }}
        .banner-tag {{ display: inline-block; background: rgba(255,255,255,0.16); padding: 4px 12px; border-radius: 999px; font-size: 12px; font-weight: 600; letter-spacing: 0.6px; text-transform: uppercase; margin-bottom: 10px; }}
        .banner h1 {{ margin: 0; font-size: 22px; font-weight: 800; letter-spacing: 0.3px; }}
        .banner p {{ margin: 6px 0 0 0; opacity: 0.85; font-size: 13.5px; }}
        .content {{ padding: 28px 32px; }}
        .info-grid {{ width: 100%; border-collapse: collapse; margin-bottom: 24px; background: #f8fafc; border-radius: 12px; overflow: hidden; border: 1px solid #e2e8f0; }}
        .info-grid td {{ padding: 10px 14px; font-size: 13.5px; border-bottom: 1px solid #e2e8f0; }}
        .info-grid tr:last-child td {{ border-bottom: none; }}
        .info-label {{ font-weight: 600; color: #64748b; width: 32%; background: #f1f5f9; }}
        .info-value {{ font-weight: 600; color: #0f172a; }}
        .kpi-table {{ width: 100%; border-collapse: separate; border-spacing: 10px 0; margin: 0 -10px 24px -10px; }}
        .kpi-card {{ background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 12px; padding: 14px; text-align: center; width: 25%; }}
        .kpi-card.highlight {{ background: #ecfdf5; border-color: #a7f3d0; }}
        .kpi-num {{ font-size: 18px; font-weight: 800; color: #0f172a; }}
        .kpi-card.highlight .kpi-num {{ color: #047857; }}
        .kpi-label {{ font-size: 11.5px; color: #64748b; margin-top: 4px; }}
        .salary-box {{ background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 12px; padding: 16px 20px; margin-bottom: 24px; }}
        .salary-box h3 {{ margin: 0 0 12px 0; font-size: 15px; color: #0f172a; }}
        .salary-row {{ display: flex; justify-content: space-between; padding: 6px 0; font-size: 13.5px; border-bottom: 1px dashed #e2e8f0; }}
        .salary-row:last-child {{ border-bottom: none; font-weight: 800; font-size: 15px; color: #047857; padding-top: 10px; }}
        table.records {{ width: 100%; border-collapse: collapse; margin-top: 10px; font-size: 13px; }}
        table.records th {{ background-color: #0f172a; color: #ffffff; padding: 10px 12px; text-align: left; font-weight: 600; }}
        table.records td {{ padding: 9px 12px; border-bottom: 1px solid #e2e8f0; }}
        table.records tr:nth-child(even) {{ background-color: #f8fafc; }}
        .badge {{ display: inline-block; padding: 3px 8px; border-radius: 999px; font-size: 11px; font-weight: 700; }}
        .badge-manual {{ background-color: #fef3c7; color: #92400e; }}
        .badge-auto {{ background-color: #dcfce7; color: #166534; }}
        .footer {{ background: #f8fafc; padding: 18px 32px; font-size: 12px; color: #64748b; border-top: 1px solid #e2e8f0; text-align: center; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='banner'>
            <div class='banner-tag'>Phiếu Báo Cáo Điện Tử</div>
            <h1>BÁO CÁO CHẤM CÔNG &amp; BẢNG LƯƠNG CÁ NHÂN</h1>
            <p>Kỳ báo cáo: <strong>{periodTitle}</strong> &bull; Ngày xuất: {VnTime.Now:dd/MM/yyyy HH:mm}</p>
        </div>
        <div class='content'>
            <table class='info-grid'>
                <tr>
                    <td class='info-label'>Họ và tên nhân viên</td>
                    <td class='info-value'>{report.FullName}</td>
                    <td class='info-label'>Phòng ban</td>
                    <td class='info-value'>{report.Department ?? "Chưa phân ban"}</td>
                </tr>
                <tr>
                    <td class='info-label'>Chức vụ</td>
                    <td class='info-value'>{report.Position ?? "Nhân viên"}</td>
                    <td class='info-label'>Email</td>
                    <td class='info-value'>{report.Email}</td>
                </tr>
            </table>

            <table class='kpi-table'>
                <tr>
                    <td class='kpi-card'>
                        <div class='kpi-num'>{report.TotalDaysPresent} ngày</div>
                        <div class='kpi-label'>Số ngày có mặt</div>
                    </td>
                    <td class='kpi-card'>
                        <div class='kpi-num'>{report.TotalHoursWorked:0.##}h</div>
                        <div class='kpi-label'>Tổng giờ làm</div>
                    </td>
                    <td class='kpi-card'>
                        <div class='kpi-num'>{otHours:0.##}h</div>
                        <div class='kpi-label'>Giờ tăng ca (OT)</div>
                    </td>
                    <td class='kpi-card highlight'>
                        <div class='kpi-num'>{totalSalary:N0} đ</div>
                        <div class='kpi-label'>Tổng lương thực nhận</div>
                    </td>
                </tr>
            </table>

            <div class='salary-box'>
                <h3>Chi tiết cách tính lương trong kỳ</h3>
                <table style='width:100%; border-collapse:collapse; font-size:13.5px;'>
                    <tr>
                        <td style='padding:6px 0; color:#475569;'>1. Giờ làm tiêu chuẩn (&le; 8h/ngày): <strong>{regHours:0.##} giờ</strong> &times; {hourlyRate:N0} đ/giờ</td>
                        <td style='padding:6px 0; text-align:right; font-weight:700;'>{regSalary:N0} VNĐ</td>
                    </tr>
                    <tr>
                        <td style='padding:6px 0; color:#475569;'>2. Giờ làm tăng ca OT (&gt; 8h/ngày): <strong>{otHours:0.##} giờ</strong> &times; {hourlyRate:N0} đ/giờ &times; hệ số {otMul:0.##}</td>
                        <td style='padding:6px 0; text-align:right; font-weight:700; color:#d97706;'>{otSalary:N0} VNĐ</td>
                    </tr>
                    {(bonusSalary > 0 ? $@"<tr>
                        <td style='padding:6px 0; color:#475569;'>3. ⭐ Thưởng cống hiến & đóng góp: <strong>{bonusReason}</strong></td>
                        <td style='padding:6px 0; text-align:right; font-weight:700; color:#059669;'>+{bonusSalary:N0} VNĐ</td>
                    </tr>" : "")}
                    <tr style='border-top:1px solid #cbd5e1;'>
                        <td style='padding:10px 0 2px 0; font-weight:800; font-size:15px; color:#0f172a;'>TỔNG LƯƠNG THỰC LĨNH</td>
                        <td style='padding:10px 0 2px 0; text-align:right; font-weight:800; font-size:16px; color:#047857;'>{totalSalary:N0} VNĐ</td>
                    </tr>
                </table>
            </div>

            <h3 style='font-size: 15px; color: #0f172a; margin: 0 0 8px 0;'>Bảng kê chi tiết các ngày chấm công ({report.FromDate:dd/MM/yyyy} - {report.ToDate:dd/MM/yyyy})</h3>
            <table class='records'>
                <thead>
                    <tr>
                        <th>Ngày</th>
                        <th>Giờ vào</th>
                        <th>Giờ về</th>
                        <th>Tổng giờ</th>
                        <th>Phương thức</th>
                        <th>Ghi chú</th>
                    </tr>
                </thead>
                <tbody>");

            if (report.DailyRecords.Count == 0)
            {
                sb.Append("<tr><td colspan='6' style='text-align: center; color: #94a3b8; padding: 18px;'>Không có bản ghi chấm công trong kỳ này.</td></tr>");
            }
            else
            {
                foreach (var day in report.DailyRecords)
                {
                    var badge = day.IsManual
                        ? "<span class='badge badge-manual'>Chấm công bù</span>"
                        : "<span class='badge badge-auto'>Vân tay</span>";

                    sb.Append($@"
                    <tr>
                        <td><strong>{day.Date:dd/MM/yyyy}</strong></td>
                        <td>{(day.CheckInTime.HasValue ? day.CheckInTime.Value.ToString("HH:mm:ss") : "--:--")}</td>
                        <td>{(day.CheckOutTime.HasValue ? day.CheckOutTime.Value.ToString("HH:mm:ss") : "Chưa ra")}</td>
                        <td style='font-weight: 700;'>{day.HoursWorked:0.##}h</td>
                        <td>{badge}</td>
                        <td style='color:#64748b;'>{day.Remarks ?? ""}</td>
                    </tr>");
                }
            }

            sb.Append(@"
                </tbody>
            </table>
        </div>
        <div class='footer'>
            Báo cáo được tạo tự động từ Hệ thống Quản trị Nhân sự &amp; Chấm công Sinh trắc học Doanh nghiệp.<br/>
            Nếu có sai sót về số giờ làm việc, vui lòng liên hệ Quản lý trực tiếp hoặc Phòng Nhân sự để được đối soát.
        </div>
    </div>
</body>
</html>");

            return sb.ToString();
        }
    }
}
