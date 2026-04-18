using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GuardianLens.API.Services;

/// <summary>
/// Notification Service — Twilio SMS + SendGrid Email
///
/// Setup (one-time, free tier works for demo):
///
///   TWILIO SMS:
///     1. Sign up at twilio.com (free trial gives $15 credit)
///     2. Get your Account SID, Auth Token, and a phone number
///     3. Add to appsettings.json under "Twilio" section
///
///   SENDGRID EMAIL:
///     1. Sign up at sendgrid.com (free tier: 100 emails/day)
///     2. Create an API key (Settings → API Keys)
///     3. Add to appsettings.json under "SendGrid" section
/// </summary>
public interface INotificationService
{
    Task SendAlertAsync(AlertPayload alert);
    Task SendEmailAsync(string to, string subject, string body);
    Task SendSmsAsync(string toPhone, string message);
}

public class NotificationService : INotificationService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(HttpClient http, IConfiguration config,
                                ILogger<NotificationService> logger)
    { _http = http; _config = config; _logger = logger; }

    // ─── Main Alert Router ────────────────────────────────────────────────────

    public async Task SendAlertAsync(AlertPayload alert)
    {
        var tasks = new List<Task>();

        // Always send email for High/Critical alerts
        if (alert.Severity >= AlertSeverity.High)
        {
            var recipients = _config.GetSection("Notifications:EmailRecipients")
                                    .Get<string[]>() ?? new[] { "admin@guardianlens.app" };

            foreach (var email in recipients)
            {
                tasks.Add(SendEmailAsync(
                    to:      email,
                    subject: $"[GuardianLens {alert.Severity}] {alert.Message[..Math.Min(60, alert.Message.Length)]}",
                    body:    BuildEmailBody(alert)
                ));
            }
        }

        // Send SMS for Critical alerts only (to avoid spam)
        if (alert.Severity == AlertSeverity.Critical)
        {
            var phones = _config.GetSection("Notifications:SmsRecipients")
                                 .Get<string[]>() ?? Array.Empty<string>();
            foreach (var phone in phones)
            {
                tasks.Add(SendSmsAsync(phone, BuildSmsMessage(alert)));
            }
        }

        await Task.WhenAll(tasks);
    }

    // ─── SendGrid Email ───────────────────────────────────────────────────────

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var apiKey = _config["SendGrid:ApiKey"];

        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_SENDGRID_API_KEY")
        {
            // Demo mode: just log
            _logger.LogInformation("[EMAIL DEMO] To: {To} | Subject: {Subject}", to, subject);
            _logger.LogDebug("[EMAIL BODY]\n{Body}", body[..Math.Min(200, body.Length)]);
            return;
        }

        var fromEmail = _config["SendGrid:FromEmail"] ?? "alerts@guardianlens.app";
        var fromName  = _config["SendGrid:FromName"]  ?? "GuardianLens Alerts";

        // SendGrid v3 Mail Send API
        var payload = new
        {
            personalizations = new[]
            {
                new { to = new[] { new { email = to } } }
            },
            from    = new { email = fromEmail, name = fromName },
            subject = subject,
            content = new[]
            {
                new { type = "text/plain", value = body },
                new { type = "text/html",  value = BuildHtmlEmail(subject, body) }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.sendgrid.com/v3/mail/send")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _http.SendAsync(request);

        if (response.IsSuccessStatusCode)
            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("SendGrid failed [{Status}]: {Error}", response.StatusCode, error);
        }
    }

    // ─── Twilio SMS ───────────────────────────────────────────────────────────

    public async Task SendSmsAsync(string toPhone, string message)
    {
        var accountSid = _config["Twilio:AccountSid"];
        var authToken  = _config["Twilio:AuthToken"];
        var fromPhone  = _config["Twilio:FromPhone"];

        if (string.IsNullOrEmpty(accountSid) || accountSid == "YOUR_TWILIO_ACCOUNT_SID")
        {
            // Demo mode: just log
            _logger.LogInformation("[SMS DEMO] To: {Phone} | {Message}", toPhone, message);
            return;
        }

        // Twilio REST API — Basic Auth with AccountSid:AuthToken
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("To",   toPhone),
            new KeyValuePair<string, string>("From", fromPhone ?? ""),
            new KeyValuePair<string, string>("Body", message)
        });

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json")
        {
            Content = formData
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var response = await _http.SendAsync(request);

        if (response.IsSuccessStatusCode)
            _logger.LogInformation("SMS sent to {Phone}", toPhone);
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Twilio SMS failed [{Status}]: {Error}", response.StatusCode, error[..100]);
        }
    }

    // ─── Message Builders ─────────────────────────────────────────────────────

    private static string BuildSmsMessage(AlertPayload alert)
        => $"⚠ GuardianLens {alert.Severity.ToString().ToUpper()}\n" +
           $"{alert.Message}\n" +
           $"URL: {alert.Url[..Math.Min(60, alert.Url.Length)]}...\n" +
           $"Dashboard: https://guardianlens.app/violations/{alert.ViolationId}";

    private static string BuildEmailBody(AlertPayload alert)
        => $"""
            GuardianLens Alert — {alert.Severity}
            =====================================

            {alert.Message}

            Details:
              Violation ID : {alert.ViolationId}
              Asset ID     : {alert.AssetId}
              Infringing   : {alert.Url}
              Severity     : {alert.Severity}
              Time         : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

            Action Required:
              View in dashboard: https://guardianlens.app/violations/{alert.ViolationId}
              Send takedown:     POST /api/violations/{alert.ViolationId}/takedown

            ---
            GuardianLens Automated IP Protection
            """;

    private static string BuildHtmlEmail(string subject, string plainBody)
    {
        var escapedBody = plainBody.Replace("\n", "<br>").Replace("  ", "&nbsp;&nbsp;");
        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"></head>
            <body style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
              <div style="background:#0B1426;padding:16px 24px;border-radius:8px 8px 0 0;">
                <h1 style="color:#fff;font-size:20px;margin:0;">🛡 GuardianLens</h1>
              </div>
              <div style="border:1px solid #E2E8F0;border-top:none;padding:24px;border-radius:0 0 8px 8px;">
                <h2 style="color:#1E293B;font-size:16px;">{subject}</h2>
                <div style="font-size:14px;color:#334155;line-height:1.6;">{escapedBody}</div>
                <div style="margin-top:24px;padding-top:16px;border-top:1px solid #E2E8F0;
                            font-size:12px;color:#64748B;">
                  Sent by GuardianLens Automated IP Protection System
                </div>
              </div>
            </body>
            </html>
            """;
    }
}
