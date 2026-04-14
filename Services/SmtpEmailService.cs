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
        var host = _config["Email:Host"];
        var portStr = _config["Email:Port"];
        var user = _config["Email:Username"];
        var pass = _config["Email:Password"];
        fromEmail = _config["Smtp:From"] ?? _config["Email:From"] ?? user;

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(portStr)) return null;
        if (!int.TryParse(portStr, out var port)) return null;

        var enableSsl = true;
        if (bool.TryParse(_config["Email:EnableSsl"], out var parsedSsl))
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

    public async Task<bool> SendCredentialsEmailAsync(string toEmail, string fullName, string username, string password, string loginUrl)
    {
        var client = TryCreateClient(out var from);
        if (client == null)
        {
            _logger.LogInformation("SMTP not configured. Credentials for user '{Username}' would be sent.", username);
            return false;
        }

        var body = $@"Hello {fullName},

Your AIInsights account has been created. Here are your login credentials:

  Username: {username}
  Password: {password}
  Login URL: {loginUrl}

Please change your password after first login.

Regards,
AIInsights Team";

        var mail = new MailMessage(from ?? "sales@aiinsights.io", toEmail)
        {
            Subject = "Your AIInsights Account Credentials",
            Body = body
        };

        try
        {
            await client.SendMailAsync(mail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send credentials email");
            return false;
        }
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string fullName, string newPassword, string loginUrl)
    {
        var client = TryCreateClient(out var from);
        if (client == null)
        {
            _logger.LogInformation("SMTP not configured. Password reset email would be sent.");
            return false;
        }

        var body = $@"Hello {fullName},

Your AIInsights password has been reset. Here are your new credentials:

  Password: {newPassword}
  Login URL: {loginUrl}

Please change your password after login.

Regards,
AIInsights Team";

        var mail = new MailMessage(from ?? "sales@aiinsights.io", toEmail)
        {
            Subject = "Your AIInsights Password Has Been Reset",
            Body = body
        };

        try
        {
            await client.SendMailAsync(mail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email");
            return false;
        }
    }
}
