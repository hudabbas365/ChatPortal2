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

    /// <summary>Tests connectivity given inbound connection details (no persisted Datasource yet).</summary>
    Task<(bool Ok, string? Error)> TestConnectionAsync(DatasourceConnectionInfo info);

    /// <summary>Returns the list of tables/objects exposed by the datasource.</summary>
    Task<(IReadOnlyList<TableInfoDto> Tables, string? Error)> GetTablesAsync(Datasource ds);

    /// <summary>Returns a flat list of distinct column/field names across the datasource.</summary>
    Task<(IReadOnlyList<string> Fields, string? Error)> GetFieldsAsync(Datasource ds);

    /// <summary>Returns full schema (tables + columns + data types) for the datasource.</summary>
    Task<(IReadOnlyList<TableSchemaDto> Tables, string? Error)> GetSchemaAsync(Datasource ds);
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
