namespace ChatPortal2.Models;

public class ChatMessage
{
    public int Id { get; set; }
    public string Role { get; set; } = "user"; // user, assistant, system
    public string Content { get; set; } = "";
    public string? GeneratedQuery { get; set; }
    public string? QueryDescription { get; set; }
    public string? ResultJson { get; set; }
    public bool IsPinned { get; set; } = false;
    public int WorkspaceId { get; set; }
    public string UserId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
