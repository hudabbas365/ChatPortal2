namespace AIInsights.Models;

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
    public string? ShareToken { get; set; } // Token for share-link viewer access
    // Monotonic counter used to revoke outstanding signed embed tokens. Each
    // outstanding embed JWT carries a `tv` claim equal to the value at mint
    // time; bumping this column instantly invalidates every previously-issued
    // token without rotating the global signing key.
    public int EmbedTokenVersion { get; set; } = 0;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
