namespace AIInsights.Models;

public class BlogAnnouncementEmailLog
{
    public int Id { get; set; }
    public int BlogId { get; set; }
    public string SubscriberEmail { get; set; } = "";
    public string Status { get; set; } = "Queued"; // Queued | Sent | Failed
    public string? ErrorMessage { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public BlogPost? Blog { get; set; }
}
