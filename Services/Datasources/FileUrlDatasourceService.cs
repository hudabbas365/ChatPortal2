using AIInsights.Models;

namespace AIInsights.Services.Datasources;

/// <summary>
/// File URL datasource handler (CSV / XLSX public links). Schema is derived
/// from the parsed file's column headers and a small sample of rows.
/// </summary>
public class FileUrlDatasourceService : IDatasourceTypeService
{
    private readonly IQueryExecutionService _queryService;

    public FileUrlDatasourceService(IQueryExecutionService queryService)
    {
        _queryService = queryService;
    }

    public bool CanHandle(string? type) =>
        !string.IsNullOrEmpty(type) && QueryExecutionService.FileUrlTypes.Contains(type);

    public Task<(bool Ok, string? Error)> TestConnectionAsync(DatasourceConnectionInfo info) =>
        _queryService.TestFileUrlAsync(info.ApiUrl);

    public async Task<(IReadOnlyList<TableInfoDto> Tables, string? Error)> GetTablesAsync(Datasource ds)
    {
        var tableName = (ds.Name ?? "").Replace(" ", "_");
        try
        {
            var fileResult = await _queryService.ExecuteFileUrlAsync(ds);
            if (fileResult.Success)
                return (new[] { new TableInfoDto(tableName, "File", fileResult.RowCount) }, null);

            return (
                new[] { new TableInfoDto(tableName, "File", 0, fileResult.Error ?? "File could not be parsed.") },
                null);
        }
        catch (Exception ex)
        {
            return (
                new[] { new TableInfoDto(tableName, "File", 0, $"File URL connection failed: {ex.Message}") },
                null);
        }
    }

    public async Task<(IReadOnlyList<string> Fields, string? Error)> GetFieldsAsync(Datasource ds)
    {
        try
        {
            var fileResult = await _queryService.ExecuteFileUrlAsync(ds);
            if (fileResult.Success && fileResult.Data.Count > 0)
            {
                var fields = fileResult.Data.First().Keys.ToList();
                if (fields.Count > 0) return (fields, null);
            }
            return (Array.Empty<string>(), fileResult.Error ?? "File could not be parsed.");
        }
        catch (Exception ex)
        {
            return (Array.Empty<string>(), ex.Message);
        }
    }

    public async Task<(IReadOnlyList<TableSchemaDto> Tables, string? Error)> GetSchemaAsync(Datasource ds)
    {
        try
        {
            var fileResult = await _queryService.ExecuteFileUrlAsync(ds, 5);
            if (fileResult.Success && fileResult.Data.Count > 0)
            {
                var firstRow = fileResult.Data.First();
                var columns = firstRow.Select(kv => new ColumnSchemaDto(
                        kv.Key,
                        DatasourceTypeJsonHelpers.InferJsonType(kv.Value),
                        false))
                    .ToList();

                var name = (ds.Name ?? "").Replace(" ", "_");
                return (new[] { new TableSchemaDto(name, "File", columns) }, null);
            }
            return (Array.Empty<TableSchemaDto>(), fileResult.Error);
        }
        catch
        {
            return (Array.Empty<TableSchemaDto>(), null);
        }
    }
}
