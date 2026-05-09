namespace AIInsights.Models;

public class BlogPost
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Author { get; set; }
    public string? ImageUrl { get; set; }
    public string? FeaturedImagePath { get; set; }
    public string? SeoKeywords { get; set; }
    public bool IsFeatureAnnouncement { get; set; }
    public string? EmailSubject { get; set; }
    public bool SendToAllSubscribers { get; set; }
    public DateTime? AnnouncementQueuedAt { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public bool IsPublished { get; set; } = true;
    public List<BlogImage> BlogImages { get; set; } = new();
    public List<BlogSubscription> BlogSubscriptions { get; set; } = new();
    public List<BlogAnnouncementEmailLog> AnnouncementEmailLogs { get; set; } = new();
}
