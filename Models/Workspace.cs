namespace ChatPortal2.Models;

public class Workspace
{
    public int Id { get; set; }
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? OwnerId { get; set; }
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public ApplicationUser? Owner { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
