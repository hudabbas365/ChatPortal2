using System.Net;
using System.Net.Mail;

namespace AIInsights.SuperAdmin.Services;

public class SmtpFeatureAnnouncementEmailSender : IFeatureAnnouncementEmailSender
{
    private readonly IConfiguration _configuration;

    public SmtpFeatureAnnouncementEmailSender(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var host = _configuration["Email:Smtp:Host"] ?? _configuration["Smtp:Host"];
        var port = int.TryParse(_configuration["Email:Smtp:Port"] ?? _configuration["Smtp:Port"], out var parsedPort)
            ? parsedPort
            : 587;
        var userName = _configuration["Email:Smtp:UserName"] ?? _configuration["Smtp:User"] ?? _configuration["Smtp:Username"];
        var password = _configuration["Email:Smtp:Password"] ?? _configuration["Smtp:Pass"] ?? _configuration["Smtp:Password"];
        var fromAddress = _configuration["Email:Smtp:FromAddress"] ?? _configuration["Smtp:From"] ?? userName;
        var fromName = _configuration["Email:Smtp:FromName"] ?? "AI Insights 365";
        var enableSsl = bool.TryParse(_configuration["Email:Smtp:EnableSsl"] ?? _configuration["Smtp:EnableSsl"] ?? _configuration["Smtp:UseSsl"], out var ssl)
            ? ssl
            : true;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
            throw new InvalidOperationException("SMTP is not configured. Please configure Email:Smtp settings.");

        using var smtp = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl
        };

        if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password))
            smtp.Credentials = new NetworkCredential(userName, password);

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(toEmail);

        cancellationToken.ThrowIfCancellationRequested();
        await smtp.SendMailAsync(message, cancellationToken);
    }
}
