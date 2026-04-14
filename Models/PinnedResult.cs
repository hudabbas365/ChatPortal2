namespace AIInsights.Models;

public class PinnedResult
{
    public int Id { get; set; }
    public string DatasetName { get; set; } = "";
    public string JsonData { get; set; } = "[]";
    public int ChatMessageId { get; set; }
    public int WorkspaceId { get; set; }
    public string UserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
