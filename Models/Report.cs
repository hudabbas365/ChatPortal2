namespace ChatPortal2.Models;

public class Report
{
    public int Id { get; set; }
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string Name { get; set; } = "Untitled Report";
    public int WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public int? DashboardId { get; set; }
    public Dashboard? Dashboard { get; set; }
    public int? DatasourceId { get; set; }
    public Datasource? Datasource { get; set; }
    public int? AgentId { get; set; }
    public Agent? Agent { get; set; }
    public string? ChartIds { get; set; }  // JSON array of selected chart IDs
    public string? CanvasJson { get; set; } // Snapshot of canvas state for this report
    public string Status { get; set; } = "Draft"; // Draft, Published
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
