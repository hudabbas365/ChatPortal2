using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace AIInsights.Services;

public class AnnouncementEmailSender : IAnnouncementEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnnouncementEmailSender> _logger;

    public AnnouncementEmailSender(IConfiguration configuration, ILogger<AnnouncementEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendAsync(AnnouncementEmailMessage message, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["SendGrid:ApiKey"] ?? Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return await SendWithSendGridAsync(apiKey, message, cancellationToken);
        }

        return await SendWithSmtpAsync(message, cancellationToken);
    }

    private async Task<bool> SendWithSendGridAsync(string apiKey, AnnouncementEmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var client = new SendGridClient(apiKey);
            var fromEmail = _configuration["SendGrid:FromEmail"] ?? _configuration["Smtp:From"] ?? "support@aiinsights365.net";
            var fromName = _configuration["SendGrid:FromName"] ?? "AI Insights 365";
            var sendGridMessage = MailHelper.CreateSingleEmail(
                new EmailAddress(fromEmail, fromName),
                new EmailAddress(message.ToEmail, string.IsNullOrWhiteSpace(message.ToName) ? message.ToEmail : message.ToName),
                message.Subject,
                Regex.Replace(message.HtmlBody, "<.*?>", string.Empty),
                message.HtmlBody);

            var response = await client.SendEmailAsync(sendGridMessage, cancellationToken);
            if ((int)response.StatusCode >= 300)
            {
                _logger.LogWarning("Announcement email to {Email} failed with SendGrid status {StatusCode}.", message.ToEmail, response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Announcement email to {Email} failed via SendGrid.", message.ToEmail);
            return false;
        }
    }

    private async Task<bool> SendWithSmtpAsync(AnnouncementEmailMessage message, CancellationToken cancellationToken)
    {
        var host = _configuration["Smtp:Host"] ?? _configuration["Email:Smtp:Host"];
        var portValue = _configuration["Smtp:Port"] ?? _configuration["Email:Smtp:Port"];
        var username = _configuration["Smtp:Username"] ?? _configuration["Email:Smtp:Username"];
        var password = _configuration["Smtp:Password"] ?? _configuration["Email:Smtp:Password"];
        var from = _configuration["Smtp:From"] ?? _configuration["Email:Smtp:From"] ?? username;
        var enableSsl = bool.TryParse(_configuration["Smtp:EnableSsl"] ?? _configuration["Email:Smtp:EnableSsl"], out var parsedSsl) ? parsedSsl : true;

        if (string.IsNullOrWhiteSpace(host) || !int.TryParse(portValue, out var port) || string.IsNullOrWhiteSpace(from))
        {
            _logger.LogWarning("Announcement email to {Email} was skipped because email settings are not configured.", message.ToEmail);
            return false;
        }

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Credentials = string.IsNullOrWhiteSpace(username) ? null : new NetworkCredential(username, password)
            };
            using var mail = new MailMessage(from, message.ToEmail)
            {
                Subject = message.Subject,
                Body = message.HtmlBody,
                IsBodyHtml = true
            };
            await client.SendMailAsync(mail, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Announcement email to {Email} failed via SMTP.", message.ToEmail);
            return false;
        }
    }
}
