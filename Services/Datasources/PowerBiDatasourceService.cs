using AIInsights.Models;

namespace AIInsights.Services.Datasources;

/// <summary>
/// Power BI XMLA datasource handler. Introspects via DAX <c>INFO.*</c>
/// functions through <see cref="IQueryExecutionService"/>.
/// </summary>
public class PowerBiDatasourceService : IDatasourceTypeService
{
    private const string TablesDax =
        "EVALUATE SELECTCOLUMNS(FILTER(INFO.TABLES(), NOT [IsHidden]), " +
        "\"table_name\", [Name], \"table_type\", \"Table\")";

    private const string FieldsDax =
        "EVALUATE SELECTCOLUMNS(FILTER(INFO.COLUMNS(), NOT [IsHidden]), " +
        "\"COLUMN_NAME\", [ExplicitName])";

    private const string SchemaDax =
        "EVALUATE VAR _tables = SELECTCOLUMNS(FILTER(INFO.TABLES(), NOT [IsHidden]), \"TableID\", [ID], \"table_name\", [Name]) " +
        "VAR _cols = SELECTCOLUMNS(FILTER(INFO.COLUMNS(), NOT [IsHidden]), \"TableID\", [TableID], \"column_name\", [ExplicitName], " +
        "\"data_type\", SWITCH([DataType], 2, \"String\", 6, \"Int64\", 8, \"Double\", 9, \"DateTime\", 10, \"Decimal\", 11, \"Boolean\", \"Other\")) " +
        "RETURN SELECTCOLUMNS(NATURALLEFTOUTERJOIN(_tables, _cols), \"table_name\", [table_name], \"table_type\", \"Table\", \"column_name\", [column_name], \"data_type\", [data_type])";

    private readonly IQueryExecutionService _queryService;

    public PowerBiDatasourceService(IQueryExecutionService queryService)
    {
        _queryService = queryService;
    }

    public bool CanHandle(string? type) =>
        !string.IsNullOrEmpty(type) && QueryExecutionService.PowerBiTypes.Contains(type);

    public Task<(bool Ok, string? Error)> TestConnectionAsync(DatasourceConnectionInfo info) =>
        _queryService.TestConnectionAsync(
            info.Type ?? "Power BI",
            info.ConnectionString ?? "",
            info.DbUser,
            info.DbPassword,
            info.XmlaEndpoint,
            info.MicrosoftAccountTenantId);

    public async Task<(IReadOnlyList<TableInfoDto> Tables, string? Error)> GetTablesAsync(Datasource ds)
    {
        if (string.IsNullOrWhiteSpace(ds.XmlaEndpoint))
            return (Array.Empty<TableInfoDto>(), "Datasource is missing connection settings.");

        var result = await _queryService.ExecuteReadOnlyAsync(ds, TablesDax);
        if (!result.Success) return (Array.Empty<TableInfoDto>(), result.Error);

        var tables = result.Data.Select(r =>
        {
            var name = ReadString(r, "table_name", "TABLE_NAME", "name") ?? "";
            return new TableInfoDto(name, "Table", 0);
        })
        .Where(t => !string.IsNullOrEmpty(t.Name))
        .ToList();

        return (tables, null);
    }

    public async Task<(IReadOnlyList<string> Fields, string? Error)> GetFieldsAsync(Datasource ds)
    {
        if (string.IsNullOrWhiteSpace(ds.XmlaEndpoint))
            return (Array.Empty<string>(), "Datasource is missing connection settings.");

        var result = await _queryService.ExecuteReadOnlyAsync(ds, FieldsDax);
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
        if (string.IsNullOrWhiteSpace(ds.XmlaEndpoint))
            return (Array.Empty<TableSchemaDto>(), "Datasource is missing connection settings.");

        var result = await _queryService.ExecuteReadOnlyAsync(ds, SchemaDax);
        if (!result.Success) return (Array.Empty<TableSchemaDto>(), result.Error);
        if (result.Data.Count == 0) return (Array.Empty<TableSchemaDto>(), null);

        var grouped = result.Data
            .GroupBy(r => ReadString(r, "table_name") ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g =>
            {
                var columns = g.Select(r => new ColumnSchemaDto(
                        ReadString(r, "column_name") ?? "",
                        ReadString(r, "data_type") ?? "",
                        false))
                    .Where(c => !string.IsNullOrEmpty(c.Name))
                    .ToList();
                return new TableSchemaDto(g.Key, "Table", columns);
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
