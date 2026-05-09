namespace AIInsights.Models;

public class BlogAnnouncementEmailLog
{
    public int Id { get; set; }
    public int BlogId { get; set; }
    public BlogPost? Blog { get; set; }
    public string SubscriberEmail { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public string? ErrorMessage { get; set; }
    public DateTime? SentAt { get; set; }
    public string? SubscriberUserId { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptedAt { get; set; }
}
