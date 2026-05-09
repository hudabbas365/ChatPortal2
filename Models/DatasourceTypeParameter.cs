namespace AIInsights.Models;

/// <summary>
/// A single connection field for a <see cref="DatasourceTypeDefinition"/>.
/// The <see cref="Key"/> MUST match a property on
/// <c>AIInsights.Services.Datasources.DatasourceConnectionInfo</c> (and the
/// matching field on <see cref="Datasource"/>) so the API layer can bind the
/// posted value without any per-type code:
///   connectionString | dbUser | dbPassword | xmlaEndpoint |
///   microsoftAccountTenantId | apiUrl | apiKey | apiMethod
/// </summary>
public class DatasourceTypeParameter
{
    public int Id { get; set; }

    public int DatasourceTypeDefinitionId { get; set; }
    public DatasourceTypeDefinition? Definition { get; set; }

    /// <summary>Camel-case binding key (see class summary for whitelist).</summary>
    public string Key { get; set; } = "";

    public string Label { get; set; } = "";

    /// <summary>Renderer hint: <c>text | password | url | multiline | select</c>.</summary>
    public string Type { get; set; } = "text";

    public string? Placeholder { get; set; }
    public bool Required { get; set; } = true;

    /// <summary>JSON array of strings — only used when <see cref="Type"/> = "select".</summary>
    public string? OptionsJson { get; set; }

    public string? Help { get; set; }
    public int SortOrder { get; set; }
}
