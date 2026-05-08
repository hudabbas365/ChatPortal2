namespace AIInsights.Models;

/// <summary>
/// Selects which authoring flow a workspace uses.
/// Standard: Datasource → Agent/Dashboard → Report (no transform stage).
/// Etl:      One or more Datasources → Transform (TOML) → Dashboard → Report.
/// </summary>
public enum WorkspaceType
{
    Standard = 0,
    Etl = 1
}

public class Workspace
{
    public int Id { get; set; }
    public string Guid { get; set; } = System.Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? OwnerId { get; set; }
    public int OrganizationId { get; set; }
    public WorkspaceType Type { get; set; } = WorkspaceType.Standard;
    public Organization? Organization { get; set; }
    public ApplicationUser? Owner { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
