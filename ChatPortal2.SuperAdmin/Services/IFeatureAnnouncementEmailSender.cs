namespace AIInsights.SuperAdmin.Services;

public interface IFeatureAnnouncementEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
