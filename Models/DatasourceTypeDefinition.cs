namespace AIInsights.Models;

/// <summary>
/// Centralized DB-backed definition of a datasource family (SQL, Power BI,
/// REST API, File URL, …). Drives both the workspace wizard and the Transform
/// Workbench "Add datasource" modal so adding/changing a connection field is
/// a single edit in the SuperAdmin UI — no redeploy required.
///
/// Built-in rows (IsBuiltIn = true) are seeded from the registered
/// <see cref="AIInsights.Services.Datasources.IDatasourceTypeService"/>
/// implementations on first run; admins can edit them but cannot delete them.
/// </summary>
public class DatasourceTypeDefinition
{
    public int Id { get; set; }

    /// <summary>
    /// Unique discriminator string surfaced to the API (e.g. "SQL Server",
    /// "Power BI", "REST API", "File URL"). Must match
    /// <c>IDatasourceTypeService.SupportedTypeStrings</c> for built-in rows.
    /// </summary>
    public string Type { get; set; } = "";

    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Bootstrap-icon class (e.g. "bi-database").</summary>
    public string? Icon { get; set; }

    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>
    /// When true, the connector requires the on-prem Gateway to reach the
    /// data source. The wizard surfaces gateway selection / setup help.
    /// </summary>
    public bool RequiresGateway { get; set; }

    /// <summary>Markdown / plain text shown in the wizard when RequiresGateway = true.</summary>
    public string? GatewayHelp { get; set; }

    /// <summary>True for rows seeded from code; admins cannot delete these.</summary>
    public bool IsBuiltIn { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }

    public ICollection<DatasourceTypeParameter> Parameters { get; set; }
        = new List<DatasourceTypeParameter>();
}
