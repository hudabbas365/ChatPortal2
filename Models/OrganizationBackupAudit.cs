namespace AIInsights.Models;

public class OrganizationBackupAudit
{
    public int Id { get; set; }
    public string Action { get; set; } = "";
    public string Mode { get; set; } = "";
    public string FileName { get; set; } = "";
    public int OrganizationId { get; set; }
    public string? PerformedByUserId { get; set; }
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public long FileSizeBytes { get; set; }
}
