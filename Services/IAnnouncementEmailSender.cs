namespace AIInsights.Services;

public interface IAnnouncementEmailSender
{
    Task<bool> SendAsync(AnnouncementEmailMessage message, CancellationToken cancellationToken = default);
}

public class AnnouncementEmailMessage
{
    public string ToEmail { get; set; } = "";
    public string ToName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
}
