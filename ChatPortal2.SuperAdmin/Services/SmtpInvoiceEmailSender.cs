using System.Net;
using System.Net.Mail;

namespace AIInsights.SuperAdmin.Services;

public class SmtpInvoiceEmailSender : IInvoiceEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpInvoiceEmailSender> _logger;

    public SmtpInvoiceEmailSender(IConfiguration config, ILogger<SmtpInvoiceEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    private SmtpClient? TryCreateClient(out string? fromEmail)
    {
        fromEmail = null;
        var host = _config["Smtp:Host"];
        var portStr = _config["Smtp:Port"];
        var user = _config["Smtp:User"];
        var pass = _config["Smtp:Pass"];
        fromEmail = _config["Smtp:From"] ?? user;

        if (string.IsNullOrEmpty(host) || !int.TryParse(portStr, out var port))
            return null;

        return new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = !string.IsNullOrEmpty(user)
                ? new NetworkCredential(user, pass)
                : null
        };
    }

    // Sanitize a string for safe inclusion in log messages (prevent log forging).
    private static string SanitizeForLog(string value) =>
        value.Replace('\n', '_').Replace('\r', '_');

    public async Task<bool> SendInvoiceEmailAsync(
        string toEmail,
        string invoiceNumber,
        string orgName,
        byte[] pdfBytes,
        string pdfFileName)
    {
        var safeInvoice = SanitizeForLog(invoiceNumber);

        using var client = TryCreateClient(out var from);
        if (client == null)
        {
            _logger.LogWarning("SMTP not configured. Invoice email for '{Invoice}' was not sent.", safeInvoice);
            return false;
        }

        var subject = $"Invoice {invoiceNumber} from AIInsights365";
        var body = $@"<div style='font-family:Inter,Arial,sans-serif;max-width:600px;margin:auto;'>
<h2 style='color:#1e3a5f;'>Invoice {WebUtility.HtmlEncode(invoiceNumber)}</h2>
<p>Hello,</p>
<p>Please find attached your invoice <strong>{WebUtility.HtmlEncode(invoiceNumber)}</strong> from <strong>AIInsights365</strong> for organization <strong>{WebUtility.HtmlEncode(orgName)}</strong>.</p>
<p>The invoice PDF is attached to this email.</p>
<br/>
<p>If you have any questions, please contact <a href='mailto:support@aiinsights365.net'>support@aiinsights365.net</a>.</p>
<br/><p>Regards,<br/><strong>AIInsights365 Team</strong></p>
</div>";

        using var mail = new MailMessage(from ?? "support@aiinsights365.net", toEmail)
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };

        using var ms = new MemoryStream(pdfBytes);
        using var attachment = new Attachment(ms, pdfFileName, "application/pdf");
        mail.Attachments.Add(attachment);

        try
        {
            await client.SendMailAsync(mail);
            _logger.LogInformation("Invoice email for '{Invoice}' was sent successfully.", safeInvoice);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send invoice email for '{Invoice}'.", safeInvoice);
            return false;
        }
    }
}
