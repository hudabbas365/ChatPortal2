using System.ComponentModel.DataAnnotations;

namespace AIInsights.Models;

/// <summary>
/// A reusable notification template that SuperAdmins can define and load into the compose form.
/// </summary>
public class NotificationTemplate
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = "";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    [MaxLength(40)]
    public string Type { get; set; } = "Announcement";

    [MaxLength(20)]
    public string Severity { get; set; } = "normal";

    public string? Link { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
