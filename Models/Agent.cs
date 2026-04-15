namespace AIInsights.Models;

public class Agent
{
    public int Id { get; set; }
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string SystemPrompt { get; set; } = "You are a helpful data assistant.";
    public int? DatasourceId { get; set; }
    public Datasource? Datasource { get; set; }
    public int? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
