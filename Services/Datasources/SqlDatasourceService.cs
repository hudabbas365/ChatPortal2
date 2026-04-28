using AIInsights.Models;

namespace AIInsights.Services.Datasources;

/// <summary>
/// SQL Server datasource handler. Connection-tests via the shared
/// <see cref="IQueryExecutionService"/> and introspects metadata through
/// <c>INFORMATION_SCHEMA</c>.
/// </summary>
public class SqlDatasourceService : IDatasourceTypeService
{
    private const string TablesSql =
        "SELECT TABLE_SCHEMA + '.' + TABLE_NAME as table_name, TABLE_TYPE as table_type " +
        "FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_TYPE, TABLE_SCHEMA, TABLE_NAME";

    private const string FieldsSql =
        "SELECT DISTINCT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS ORDER BY COLUMN_NAME";

    private const string SchemaSql =
        "SELECT t.TABLE_SCHEMA + '.' + t.TABLE_NAME as table_name, t.TABLE_TYPE as table_type, " +
        "c.COLUMN_NAME as column_name, c.DATA_TYPE as data_type " +
        "FROM INFORMATION_SCHEMA.TABLES t " +
        "JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME " +
        "ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION";

    private readonly IQueryExecutionService _queryService;

    public SqlDatasourceService(IQueryExecutionService queryService)
    {
        _queryService = queryService;
    }

    public bool CanHandle(string? type) =>
        !string.IsNullOrEmpty(type) && QueryExecutionService.SqlTypes.Contains(type);

    public Task<(bool Ok, string? Error)> TestConnectionAsync(DatasourceConnectionInfo info) =>
        _queryService.TestConnectionAsync(
            info.Type ?? "SQL Server",
            info.ConnectionString ?? "",
            info.DbUser,
            info.DbPassword,
            info.XmlaEndpoint,
            info.MicrosoftAccountTenantId);

    public async Task<(IReadOnlyList<TableInfoDto> Tables, string? Error)> GetTablesAsync(Datasource ds)
    {
        if (string.IsNullOrWhiteSpace(ds.ConnectionString))
            return (Array.Empty<TableInfoDto>(), "Datasource is missing connection settings.");

        var result = await _queryService.ExecuteReadOnlyAsync(ds, TablesSql);
        if (!result.Success) return (Array.Empty<TableInfoDto>(), result.Error);

        var tables = result.Data.Select(r =>
        {
            var name = ReadString(r, "table_name", "TABLE_NAME", "name") ?? "";
            var rawType = ReadString(r, "table_type", "TABLE_TYPE", "type") ?? "Table";
            var type = rawType.Contains("VIEW", StringComparison.OrdinalIgnoreCase) ? "View" : "Table";
            return new TableInfoDto(name, type, 0);
        })
        .Where(t => !string.IsNullOrEmpty(t.Name))
        .ToList();

        return (tables, null);
    }

    public async Task<(IReadOnlyList<string> Fields, string? Error)> GetFieldsAsync(Datasource ds)
    {
        if (string.IsNullOrWhiteSpace(ds.ConnectionString))
            return (Array.Empty<string>(), "Datasource is missing connection settings.");

        var result = await _queryService.ExecuteReadOnlyAsync(ds, FieldsSql);
        if (!result.Success) return (Array.Empty<string>(), result.Error);

        var fields = result.Data
            .Select(r => r.Values.First()?.ToString() ?? "")
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct()
            .ToList();

        return (fields, null);
    }

    public async Task<(IReadOnlyList<TableSchemaDto> Tables, string? Error)> GetSchemaAsync(Datasource ds)
    {
        if (string.IsNullOrWhiteSpace(ds.ConnectionString))
            return (Array.Empty<TableSchemaDto>(), "Datasource is missing connection settings.");

        var result = await _queryService.ExecuteReadOnlyAsync(ds, SchemaSql);
        if (!result.Success) return (Array.Empty<TableSchemaDto>(), result.Error);
        if (result.Data.Count == 0) return (Array.Empty<TableSchemaDto>(), null);

        var grouped = result.Data
            .GroupBy(r => ReadString(r, "table_name") ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g =>
            {
                var rawType = ReadString(g.First(), "table_type") ?? "Table";
                var type = rawType.Contains("VIEW", StringComparison.OrdinalIgnoreCase) ? "View" : "Table";
                var columns = g.Select(r => new ColumnSchemaDto(
                        ReadString(r, "column_name") ?? "",
                        ReadString(r, "data_type") ?? "",
                        false))
                    .Where(c => !string.IsNullOrEmpty(c.Name))
                    .ToList();
                return new TableSchemaDto(g.Key, type, columns);
            })
            .ToList();

        return (grouped, null);
    }

    private static string? ReadString(IReadOnlyDictionary<string, object> row, params string[] keys)
    {
        foreach (var k in keys)
            if (row.TryGetValue(k, out var v) && v != null) return v.ToString();
        return null;
    }
}
