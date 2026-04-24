using System.Net;
using System.Net.Mail;

namespace AIInsights.SuperAdmin.Services;

/// <summary>
/// SMTP-based implementation of <see cref="IUrgentNotificationEmailer"/>.
/// Reads configuration from appsettings.json under the "Smtp" section:
///   Smtp:Host, Smtp:Port, Smtp:User, Smtp:Pass, Smtp:From, Smtp:UseSsl
/// </summary>
public class SmtpUrgentNotificationEmailer : IUrgentNotificationEmailer
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpUrgentNotificationEmailer> _logger;

    public SmtpUrgentNotificationEmailer(IConfiguration config, ILogger<SmtpUrgentNotificationEmailer> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toEmail, string toName, string notificationTitle,
        string notificationBody, string clickUrl, CancellationToken cancellationToken = default)
    {
        var host = _config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning("SMTP host not configured. Skipping urgent email to {Email}.", toEmail);
            return false;
        }

        var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
        var user = _config["Smtp:User"] ?? "";
        var pass = _config["Smtp:Pass"] ?? "";
        var from = _config["Smtp:From"] ?? user;
        var useSsl = string.Equals(_config["Smtp:UseSsl"], "true", StringComparison.OrdinalIgnoreCase);

        var subject = $"[URGENT] {notificationTitle}";
        var html = BuildHtml(notificationTitle, notificationBody, clickUrl);

        try
        {
            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(user, pass),
                EnableSsl = useSsl
            };

            var msg = new MailMessage(from, toEmail, subject, html)
            {
                IsBodyHtml = true
            };
            if (!string.IsNullOrWhiteSpace(toName))
                msg.To.Clear();
            msg.To.Add(new MailAddress(toEmail, toName));

            await client.SendMailAsync(msg, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send urgent email to {Email}.", toEmail);
            return false;
        }
    }

    private static string BuildHtml(string title, string body, string clickUrl)
    {
        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"/></head>
            <body style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;">
                <div style="background:#dc3545;color:#fff;padding:12px 20px;border-radius:6px 6px 0 0;">
                    <strong>🔔 URGENT Notification</strong>
                </div>
                <div style="border:1px solid #dee2e6;border-top:none;padding:20px;border-radius:0 0 6px 6px;">
                    <h2 style="margin-top:0;color:#212529;">{HtmlEncode(title)}</h2>
                    <p style="color:#495057;">{HtmlEncode(body)}</p>
                    {(string.IsNullOrWhiteSpace(clickUrl) ? "" : $"""
                    <p>
                        <a href="{clickUrl}" style="display:inline-block;background:#0d6efd;color:#fff;
                           padding:10px 20px;border-radius:4px;text-decoration:none;font-weight:bold;">
                            Open Notification
                        </a>
                    </p>
                    """)}
                    <hr style="border:none;border-top:1px solid #dee2e6;margin:20px 0;"/>
                    <p style="color:#6c757d;font-size:12px;">
                        This is an automated urgent notification. Please do not reply to this email.
                    </p>
                </div>
            </body>
            </html>
            """;
    }

    private static string HtmlEncode(string s) =>
        System.Net.WebUtility.HtmlEncode(s);
}
