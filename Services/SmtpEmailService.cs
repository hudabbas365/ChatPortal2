using System.Net;
using System.Net.Mail;

namespace AIInsights.Services;

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    private SmtpClient? TryCreateClient(out string? fromEmail)
    {
        fromEmail = null;
        var host = _config["Smtp:Host"] ?? _config["Email:Host"];
        var portStr = _config["Smtp:Port"]?.ToString() ?? _config["Email:Port"];
        var user = _config["Smtp:Username"] ?? _config["Email:Username"];
        var pass = _config["Smtp:Password"] ?? _config["Email:Password"];
        fromEmail = _config["Smtp:From"] ?? _config["Email:From"] ?? user;

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(portStr)) return null;
        if (!int.TryParse(portStr, out var port)) return null;

        var enableSsl = true;
        if (bool.TryParse(_config["Smtp:EnableSsl"] ?? _config["Email:EnableSsl"], out var parsedSsl))
            enableSsl = parsedSsl;

        var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            Credentials = !string.IsNullOrEmpty(user)
                ? new NetworkCredential(user, pass)
                : null
        };
        return client;
    }

    private async Task<bool> SendHtmlEmailAsync(string toEmail, string subject, string htmlBody)
    {
        var client = TryCreateClient(out var from);
        if (client == null)
        {
            _logger.LogWarning("SMTP not configured. Email '{Subject}' to '{To}' was not sent.", subject, toEmail);
            return false;
        }

        var mail = new MailMessage(from ?? "support@aiinsights365.net", toEmail)
        {
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };

        try
        {
            await client.SendMailAsync(mail);
            _logger.LogInformation("Email '{Subject}' sent to '{To}'.", subject, toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email '{Subject}' to '{To}'.", subject, toEmail);
            return false;
        }
    }

    public async Task<bool> SendCredentialsEmailAsync(string toEmail, string fullName, string username, string password, string loginUrl)
    {
        var body = $@"<div style='font-family:Inter,Arial,sans-serif;max-width:600px;margin:auto;'>
<h2 style='color:#1e3a5f;'>Welcome to AI Insights 365</h2>
<p>Hello {fullName},</p>
<p>Your account has been created.</p>
<p><strong>Username:</strong> {username}<br/><strong>Login:</strong> <a href='{loginUrl}'>{loginUrl}</a></p>
<p>Please use the Forgot Password feature on the login page to set your password.</p>
<br/><p>Regards,<br/><strong>AI Insights 365 Team</strong></p></div>";
        return await SendHtmlEmailAsync(toEmail, "Your AI Insights 365 Account", body);
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string fullName, string newPassword, string loginUrl)
    {
        var body = $@"<div style='font-family:Inter,Arial,sans-serif;max-width:600px;margin:auto;'>
<h2 style='color:#1e3a5f;'>Password Reset — AI Insights 365</h2>
<p>Hello {fullName},</p>
<p>Your password has been reset by an administrator.</p>
<p>Please use the Forgot Password feature at <a href='{loginUrl}'>{loginUrl}</a> to set a new password.</p>
<br/><p>Regards,<br/><strong>AI Insights 365 Team</strong></p></div>";
        return await SendHtmlEmailAsync(toEmail, "Your AI Insights 365 Password Has Been Reset", body);
    }

    public async Task<bool> SendEmailConfirmationAsync(string toEmail, string fullName, string confirmUrl)
    {
        var body = $@"<div style='font-family:Inter,Arial,sans-serif;max-width:600px;margin:auto;'>
<h2 style='color:#1e3a5f;'>Verify Your Email — AI Insights 365</h2>
<p>Hello {fullName},</p>
<p>Thank you for registering with AI Insights 365! Please confirm your email address by clicking the button below:</p>
<p style='text-align:center;margin:30px 0;'>
  <a href='{confirmUrl}' style='background:linear-gradient(135deg,#667eea,#764ba2);color:#fff;padding:14px 32px;border-radius:8px;text-decoration:none;font-weight:600;font-size:16px;'>
    Confirm Email Address
  </a>
</p>
<p style='color:#666;font-size:13px;'>Or copy and paste this link into your browser:<br/><a href='{confirmUrl}'>{confirmUrl}</a></p>
<p style='color:#666;font-size:13px;'>This link expires in 24 hours.</p>
<br/><p>Regards,<br/><strong>AI Insights 365 Team</strong></p></div>";
        return await SendHtmlEmailAsync(toEmail, "Confirm Your Email — AI Insights 365", body);
    }

    public async Task<bool> SendForgotPasswordEmailAsync(string toEmail, string fullName, string resetUrl)
    {
        var body = $@"<div style='font-family:Inter,Arial,sans-serif;max-width:600px;margin:auto;'>
<h2 style='color:#1e3a5f;'>Reset Your Password — AI Insights 365</h2>
<p>Hello {fullName},</p>
<p>We received a request to reset your password. Click the button below to set a new password:</p>
<p style='text-align:center;margin:30px 0;'>
  <a href='{resetUrl}' style='background:linear-gradient(135deg,#667eea,#764ba2);color:#fff;padding:14px 32px;border-radius:8px;text-decoration:none;font-weight:600;font-size:16px;'>
    Reset Password
  </a>
</p>
<p style='color:#666;font-size:13px;'>Or copy and paste this link into your browser:<br/><a href='{resetUrl}'>{resetUrl}</a></p>
<p style='color:#666;font-size:13px;'>This link expires in 1 hour. If you didn't request this, please ignore this email.</p>
<br/><p>Regards,<br/><strong>AI Insights 365 Team</strong></p></div>";
        return await SendHtmlEmailAsync(toEmail, "Reset Your Password — AI Insights 365", body);
    }

    public async Task<bool> SendInvoiceEmailAsync(string toEmail, string fullName, string orgName, string description, decimal amount, string currency, string paymentId, DateTime date)
    {
        var body = $@"<div style='font-family:Inter,Arial,sans-serif;max-width:600px;margin:auto;'>
<h2 style='color:#1e3a5f;'>Payment Invoice — AI Insights 365</h2>
<p>Hello {fullName},</p>
<p>Thank you for your purchase! Here is your invoice summary:</p>
<table style='width:100%;border-collapse:collapse;margin:20px 0;'>
  <tr style='border-bottom:2px solid #e2e8f0;'><td style='padding:10px;font-weight:600;'>Organization</td><td style='padding:10px;'>{orgName}</td></tr>
  <tr style='border-bottom:1px solid #e2e8f0;'><td style='padding:10px;font-weight:600;'>Description</td><td style='padding:10px;'>{description}</td></tr>
  <tr style='border-bottom:1px solid #e2e8f0;'><td style='padding:10px;font-weight:600;'>Amount</td><td style='padding:10px;'>{amount:C2} {currency}</td></tr>
  <tr style='border-bottom:1px solid #e2e8f0;'><td style='padding:10px;font-weight:600;'>Payment ID</td><td style='padding:10px;'>{paymentId}</td></tr>
  <tr><td style='padding:10px;font-weight:600;'>Date</td><td style='padding:10px;'>{date:MMMM dd, yyyy HH:mm} UTC</td></tr>
</table>
<p style='color:#666;font-size:13px;'>If you have any questions about this invoice, please contact <a href='mailto:support@aiinsights365.net'>support@aiinsights365.net</a>.</p>
<br/><p>Regards,<br/><strong>AI Insights 365 Team</strong></p></div>";
        return await SendHtmlEmailAsync(toEmail, $"Invoice — AI Insights 365 ({amount:C2})", body);
    }

    public async Task<bool> SendSupportTicketToSupportAsync(string ticketNumber, string requesterName, string requesterEmail, string category, string priority, string subject, string message, string? orgName)
    {
        var supportInbox = _config["Support:Email"] ?? "support@AIInsights365.net";
        var safeMsg = System.Net.WebUtility.HtmlEncode(message ?? string.Empty).Replace("\n", "<br/>");
        var body = $@"<div style='font-family:Inter,Arial,sans-serif;max-width:680px;margin:auto;'>
<h2 style='color:#1e3a5f;'>New Support Ticket — {ticketNumber}</h2>
<table style='width:100%;border-collapse:collapse;margin:16px 0;'>
  <tr><td style='padding:8px;font-weight:600;width:160px;'>Ticket #</td><td style='padding:8px;'>{ticketNumber}</td></tr>
  <tr><td style='padding:8px;font-weight:600;'>From</td><td style='padding:8px;'>{System.Net.WebUtility.HtmlEncode(requesterName)} &lt;{System.Net.WebUtility.HtmlEncode(requesterEmail)}&gt;</td></tr>
  <tr><td style='padding:8px;font-weight:600;'>Organization</td><td style='padding:8px;'>{System.Net.WebUtility.HtmlEncode(orgName ?? "(public submission)")}</td></tr>
  <tr><td style='padding:8px;font-weight:600;'>Category</td><td style='padding:8px;'>{System.Net.WebUtility.HtmlEncode(category)}</td></tr>
  <tr><td style='padding:8px;font-weight:600;'>Priority</td><td style='padding:8px;'>{System.Net.WebUtility.HtmlEncode(priority)}</td></tr>
  <tr><td style='padding:8px;font-weight:600;'>Subject</td><td style='padding:8px;'>{System.Net.WebUtility.HtmlEncode(subject)}</td></tr>
</table>
<div style='background:#f8fafc;border-left:4px solid #1e3a5f;padding:14px;border-radius:6px;'>{safeMsg}</div>
<p style='color:#666;font-size:12px;margin-top:18px;'>Reply directly to <a href='mailto:{requesterEmail}'>{requesterEmail}</a> to respond to the customer.</p>
</div>";
        return await SendHtmlEmailAsync(supportInbox, $"[{priority}] {ticketNumber} — {subject}", body);
    }

    public async Task<bool> SendSupportTicketAcknowledgmentAsync(string userEmail, string userName, string ticketNumber, string subject)
    {
        var body = $@"<div style='font-family:Inter,Arial,sans-serif;max-width:600px;margin:auto;'>
<h2 style='color:#1e3a5f;'>We've received your request</h2>
<p>Hello {System.Net.WebUtility.HtmlEncode(userName)},</p>
<p>Thank you for contacting AI Insights 365 support. Your ticket has been logged and our team will respond within your plan's SLA.</p>
<table style='width:100%;border-collapse:collapse;margin:20px 0;'>
  <tr style='border-bottom:1px solid #e2e8f0;'><td style='padding:10px;font-weight:600;width:160px;'>Ticket Number</td><td style='padding:10px;font-family:monospace;color:#1e3a5f;font-size:15px;'>{ticketNumber}</td></tr>
  <tr><td style='padding:10px;font-weight:600;'>Subject</td><td style='padding:10px;'>{System.Net.WebUtility.HtmlEncode(subject)}</td></tr>
</table>
<p>Please keep this ticket number for your records — you can reference it in any follow-up email.</p>
<p style='color:#666;font-size:13px;'>SLA targets: <a href='https://aiinsights365.net/sla'>aiinsights365.net/sla</a></p>
<br/><p>Regards,<br/><strong>AI Insights 365 Support</strong></p></div>";
        return await SendHtmlEmailAsync(userEmail, $"[Ticket {ticketNumber}] We received your request", body);
    }
}
