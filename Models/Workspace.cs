namespace ChatPortal2.Models;

public class Workspace
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
