namespace AIInsights.Models;

public class WorkspaceUser
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public string Role { get; set; } = "Viewer"; // Viewer, Editor, Admin
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
