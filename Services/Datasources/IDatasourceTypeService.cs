using AIInsights.Models;

namespace AIInsights.Services.Datasources;

/// <summary>
/// Lightweight DTO carrying the connection bits of a <see cref="Datasource"/> /
/// inbound DatasourceRequest so per-type services don't need to depend on
/// controller-side request types.
/// </summary>
public class DatasourceConnectionInfo
{
    public string? Type { get; set; }
    public string? ConnectionString { get; set; }
    public string? DbUser { get; set; }
    public string? DbPassword { get; set; }
    public string? XmlaEndpoint { get; set; }
    public string? MicrosoftAccountTenantId { get; set; }
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiMethod { get; set; }
}

public record TableInfoDto(string Name, string Type, long RowCount, string? Error = null);
public record ColumnSchemaDto(string Name, string DataType, bool IsPrimaryKey);
public record TableSchemaDto(string Name, string Type, IReadOnlyList<ColumnSchemaDto> Columns);

/// <summary>
/// Describes a single connection-parameter field that the Add-Datasource UI
/// should render for a given datasource family. Centralizing this on
/// <see cref="IDatasourceTypeService"/> means a new datasource implementation
/// auto-surfaces the right fields in every client without UI edits.
/// </summary>
/// <param name="Key">camelCase property name on <c>DatasourceRequest</c> the value is bound to (e.g. "connectionString", "apiUrl").</param>
/// <param name="Label">Friendly label rendered above the input.</param>
/// <param name="Type">Renderer hint: "text" | "password" | "url" | "multiline" | "select".</param>
/// <param name="Placeholder">Optional placeholder / example text.</param>
/// <param name="Required">When true the client refuses to submit until a non-empty value is supplied.</param>
/// <param name="Options">For <c>select</c> fields: allowed values shown in the dropdown.</param>
/// <param name="Help">Optional small-print hint shown beneath the field.</param>
public record DatasourceParamDescriptor(
    string Key,
    string Label,
    string Type,
    string? Placeholder = null,
    bool Required = true,
    IReadOnlyList<string>? Options = null,
    string? Help = null);

/// <summary>
/// Per-datasource-type service. Each datasource family (SQL Server, Power BI,
/// REST API, File URL) ships its own implementation so the introspection
/// logic and connection-test logic stay isolated and easy to maintain.
///
/// Pattern mirrors <c>IAutoReportBuilder</c>: the controller resolves
/// <see cref="IEnumerable{IDatasourceTypeService}"/> from DI and picks the
/// first service whose <see cref="CanHandle"/> returns true.
/// </summary>
public interface IDatasourceTypeService
{
    /// <summary>True when this service is responsible for the given datasource type string.</summary>
    bool CanHandle(string? type);

    /// <summary>
    /// Canonical display names for datasource types handled by this service.
    /// Used to build the allowed-type list returned by <c>GET /api/datasources/types</c>
    /// dynamically from the registered service collection — adding a new service
    /// automatically surfaces its type in the UI without touching the controller.
    /// </summary>
    IReadOnlyList<string> SupportedTypeStrings { get; }

    /// <summary>
    /// Connection-parameter schema for this datasource family. Drives the
    /// Add-Datasource UI: each entry becomes a labelled input whose value is
    /// posted back to <c>POST /api/datasources</c> under the matching
    /// <c>DatasourceRequest</c> property name. Adding a new
    /// <see cref="IDatasourceTypeService"/> in DI automatically extends the UI
    /// — no client-side changes required.
    /// </summary>
    IReadOnlyList<DatasourceParamDescriptor> Parameters { get; }

    /// <summary>Tests connectivity given inbound connection details (no persisted Datasource yet).</summary>
    Task<(bool Ok, string? Error)> TestConnectionAsync(DatasourceConnectionInfo info);

    /// <summary>Returns the list of tables/objects exposed by the datasource.</summary>
    Task<(IReadOnlyList<TableInfoDto> Tables, string? Error)> GetTablesAsync(Datasource ds);

    /// <summary>Returns a flat list of distinct column/field names across the datasource.</summary>
    Task<(IReadOnlyList<string> Fields, string? Error)> GetFieldsAsync(Datasource ds);

    /// <summary>Returns full schema (tables + columns + data types) for the datasource.</summary>
    Task<(IReadOnlyList<TableSchemaDto> Tables, string? Error)> GetSchemaAsync(Datasource ds);

    /// <summary>
    /// Fetches a sample of up to <paramref name="maxRows"/> rows from the datasource
    /// using the dialect/protocol appropriate for this datasource type.
    /// Used by the ETL workbench preview and multi-datasource transform preview.
    /// Returns an empty list (not an error) when no table is configured.
    /// </summary>
    Task<(IReadOnlyList<Dictionary<string, object>> Rows, string? Error)> SamplePreviewRowsAsync(Datasource ds, int maxRows);
}

internal static class DatasourceTypeJsonHelpers
{
    /// <summary>Best-effort JSON value → BI data-type tag used by REST API and File URL services.</summary>
    public static string InferJsonType(object? value)
    {
        if (value == null) return "string";
        if (value is bool) return "boolean";
        if (value is int or long or short or byte) return "integer";
        if (value is float or double or decimal) return "decimal";
        var s = value.ToString() ?? "";
        if (DateTime.TryParse(s, out _)) return "datetime";
        return "string";
    }
}
