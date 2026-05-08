namespace AIInsights.Models;

public class TransformStep
{
    public int Id { get; set; }
    public int TransformDraftId { get; set; }
    public TransformDraft? TransformDraft { get; set; }
    public string StepGuid { get; set; } = System.Guid.NewGuid().ToString();
    public string StepType { get; set; } = "";
    public string? Title { get; set; }
    public string? ParametersJson { get; set; }
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
