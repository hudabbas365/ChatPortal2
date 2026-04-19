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
    // REST API fields
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiMethod { get; set; } // GET, POST, PUT, DELETE, PATCH
    public int OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public int? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
