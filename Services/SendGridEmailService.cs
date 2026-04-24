using System.Text.RegularExpressions;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace AIInsights.Services;

public class SendGridEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(IConfiguration config, ILogger<SendGridEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    private string? ApiKey =>
        _config["SendGrid:ApiKey"]
        ?? Environment.GetEnvironmentVariable("SENDGRID_API_KEY");

    private string FromEmail =>
        _config["SendGrid:FromEmail"]
        ?? _config["Smtp:From"]
        ?? "support@aiinsights365.net";

    private string FromName =>
        _config["SendGrid:FromName"] ?? "AI Insights 365";

    private async Task<bool> SendHtmlEmailAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        var apiKey = ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("SendGrid API key missing. Email '{Subject}' to '{To}' was not sent.", subject, toEmail);
            return false;
        }

        try
        {
            var client = new SendGridClient(apiKey);
            var from = new EmailAddress(FromEmail, FromName);
            var to = new EmailAddress(toEmail, string.IsNullOrWhiteSpace(toName) ? toEmail : toName);
            var plainText = Regex.Replace(htmlBody, "<.*?>", string.Empty);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainText, htmlBody);

            var response = await client.SendEmailAsync(msg).ConfigureAwait(false);
            if ((int)response.StatusCode >= 300)
            {
                var body = await response.Body.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogWarning("SendGrid returned {Status} for '{Subject}' to '{To}': {Body}",
                    response.StatusCode, subject, toEmail, body);
                return false;
            }

            _logger.LogInformation("Email '{Subject}' sent via SendGrid to '{To}'.", subject, toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email '{Subject}' to '{To}' via SendGrid.", subject, toEmail);
            return false;
        }
    }

    public Task<bool> SendCredentialsEmailAsync(string toEmail, string fullName, string username, string password, string loginUrl)
    {
        var body = $@"<div style='font-family:Inter,Arial,sans-serif;max-width:600px;margin:auto;'>
<h2 style='color:#1e3a5f;'>Welcome to AI Insights 365</h2>
<p>Hello {fullName},</p>
<p>Your account has been created.</p>
<p><strong>Username:</strong> {username}<br/><strong>Login:</strong> <a href='{loginUrl}'>{loginUrl}</a></p>
<p>Please use the Forgot Password feature on the login page to set your password.</p>
<br/><p>Regards,<br/><strong>AI Insights 365 Team</strong></p></div>";
        return SendHtmlEmailAsync(toEmail, fullName, "Your AI Insights 365 Account", body);
    }

    public Task<bool> SendPasswordResetEmailAsync(string toEmail, string fullName, string newPassword, string loginUrl)
    {
        var body = $@"<div style='font-family:Inter,Arial,sans-serif;max-width:600px;margin:auto;'>
<h2 style='color:#1e3a5f;'>Password Reset — AI Insights 365</h2>
<p>Hello {fullName},</p>
<p>Your password has been reset by an administrator. You can sign in now using the temporary password below:</p>
<table style='width:100%;border-collapse:collapse;margin:20px 0;'>
  <tr style='border-bottom:1px solid #e2e8f0;'><td style='padding:10px;font-weight:600;width:40%;'>Email</td><td style='padding:10px;'>{toEmail}</td></tr>
  <tr style='border-bottom:1px solid #e2e8f0;'><td style='padding:10px;font-weight:600;'>Temporary Password</td><td style='padding:10px;font-family:Consolas,monospace;background:#f8fafc;border-radius:4px;'><strong>{newPassword}</strong></td></tr>
</table>
<p style='text-align:center;margin:30px 0;'>
  <a href='{loginUrl}' style='background:linear-gradient(135deg,#667eea,#764ba2);color:#fff;padding:14px 32px;border-radius:8px;text-decoration:none;font-weight:600;font-size:16px;'>
    Sign In
  </a>
</p>
<p style='color:#b45309;font-size:13px;background:#fef3c7;padding:10px;border-radius:6px;'>
  <strong>Security tip:</strong> For your protection, please sign in and change this password immediately from your account settings.
</p>
<p style='color:#666;font-size:13px;'>If you did not request this change, please contact <a href='mailto:support@aiinsights365.net'>support@aiinsights365.net</a> right away.</p>
<br/><p>Regards,<br/><strong>AI Insights 365 Team</strong></p></div>";
        return SendHtmlEmailAsync(toEmail, fullName, "Your AI Insights 365 Password Has Been Reset", body);
    }

    public Task<bool> SendEmailConfirmationAsync(string toEmail, string fullName, string confirmUrl)
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
        return SendHtmlEmailAsync(toEmail, fullName, "Confirm Your Email — AI Insights 365", body);
    }

    public Task<bool> SendForgotPasswordEmailAsync(string toEmail, string fullName, string resetUrl)
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
        return SendHtmlEmailAsync(toEmail, fullName, "Reset Your Password — AI Insights 365", body);
    }

    public Task<bool> SendInvoiceEmailAsync(string toEmail, string fullName, string orgName, string description, decimal amount, string currency, string paymentId, DateTime date)
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
        return SendHtmlEmailAsync(toEmail, fullName, $"Invoice — AI Insights 365 ({amount:C2})", body);
    }
}
