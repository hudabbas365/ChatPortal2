namespace AIInsights.Models;

public class ActivityLog
{
    public int Id { get; set; }
    public string Action { get; set; } = "";
    public string Description { get; set; } = "";
    public string UserId { get; set; } = "";
    public int? OrganizationId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
