namespace ChatPortal2.Models;

public class TokenUsage
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string UserId { get; set; } = "";
    public int TokensUsed { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
