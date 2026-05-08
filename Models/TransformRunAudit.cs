namespace AIInsights.Models;

public class TransformRunAudit
{
    public int Id { get; set; }
    public string RunGuid { get; set; } = System.Guid.NewGuid().ToString();
    public int? DatasourceId { get; set; }
    public Datasource? Datasource { get; set; }
    public int? TransformDraftId { get; set; }
    public TransformDraft? TransformDraft { get; set; }
    public bool Success { get; set; }
    public int InputRowCount { get; set; }
    public int OutputRowCount { get; set; }
    public long DurationMs { get; set; }
    public string? MessagesJson { get; set; }
    public string? Error { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
