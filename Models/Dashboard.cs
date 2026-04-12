namespace ChatPortal2.Models;

public class Dashboard
{
    public int Id { get; set; }
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string Name { get; set; } = "Dashboard";
    public int WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public int? AgentId { get; set; }
    public Agent? Agent { get; set; }
    public int? DatasourceId { get; set; }
    public Datasource? Datasource { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
