namespace ChatPortal2.Models;

public class Organization
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? LogoUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ApplicationUser> Users { get; set; } = new();
    public List<Workspace> Workspaces { get; set; } = new();
    public List<Datasource> Datasources { get; set; } = new();
    public List<Agent> Agents { get; set; } = new();
}
