namespace ChatPortal2.Models;

public class WorkspaceMemory
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public string Content { get; set; } = "";
    public string Source { get; set; } = "auto";     // "auto" | "manual"
    public string Category { get; set; } = "general"; // "fact" | "preference" | "context" | "general"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
