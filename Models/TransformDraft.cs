namespace AIInsights.Models;

public class TransformDraft
{
    public int Id { get; set; }
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public int? DatasourceId { get; set; }
    public Datasource? Datasource { get; set; }
    public string? DatasourceGuidsCsv { get; set; }
    public string Name { get; set; } = "AI Insights 365 ETL Draft";
    public string TomlDefinition { get; set; } = "";
    public string StepsJson { get; set; } = "[]";
    public string Status { get; set; } = "draft"; // draft | published
    public string? CreatedByUserId { get; set; }
    public string? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
    public string? AuditMetadataJson { get; set; }

    public List<TransformSource> Sources { get; set; } = new();
    public List<TransformStep> Steps { get; set; } = new();
}
