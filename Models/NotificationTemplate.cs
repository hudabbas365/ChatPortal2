namespace AIInsights.Models;

/// <summary>
/// A reusable notification template that SuperAdmins can define and load into the compose form.
/// </summary>
public class NotificationTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Type { get; set; } = "Announcement";
    public string Severity { get; set; } = "normal";
    public string? Link { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
