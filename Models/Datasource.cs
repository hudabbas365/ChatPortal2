namespace AIInsights.Models;

public class Datasource
{
    public int Id { get; set; }
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Type { get; set; } = "SqlServer";
    public string ConnectionString { get; set; } = "";
    public string? DbUser { get; set; }
    public string? DbPassword { get; set; }
    public string? SelectedTables { get; set; }
    public string? XmlaEndpoint { get; set; }
    public string? MicrosoftAccountTenantId { get; set; }
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public int? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
