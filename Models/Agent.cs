namespace ChatPortal2.Models;

public class Agent
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SystemPrompt { get; set; } = "You are a helpful data assistant.";
    public int? DatasourceId { get; set; }
    public Datasource? Datasource { get; set; }
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
