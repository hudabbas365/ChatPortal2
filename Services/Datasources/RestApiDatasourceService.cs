using AIInsights.Models;

namespace AIInsights.Services.Datasources;

/// <summary>
/// REST API datasource handler. The "schema" of an API is derived from a
/// sample response — keys of the first object become columns, types are
/// inferred from JSON values.
/// </summary>
public class RestApiDatasourceService : IDatasourceTypeService
{
    private readonly IQueryExecutionService _queryService;

    public RestApiDatasourceService(IQueryExecutionService queryService)
    {
        _queryService = queryService;
    }

    public bool CanHandle(string? type) =>
        !string.IsNullOrEmpty(type) && QueryExecutionService.RestApiTypes.Contains(type);

    public Task<(bool Ok, string? Error)> TestConnectionAsync(DatasourceConnectionInfo info) =>
        _queryService.TestRestApiAsync(info.ApiUrl, info.ApiKey, info.ApiMethod);

    public async Task<(IReadOnlyList<TableInfoDto> Tables, string? Error)> GetTablesAsync(Datasource ds)
    {
        var tableName = (ds.Name ?? "").Replace(" ", "_");
        try
        {
            var apiResult = await _queryService.ExecuteRestApiAsync(ds);
            if (apiResult.Success && apiResult.Data.Count > 0)
                return (new[] { new TableInfoDto(tableName, "API Endpoint", apiResult.Data.Count) }, null);

            return (
                new[] { new TableInfoDto(tableName, "API Endpoint", 0, apiResult.Error ?? "REST API returned no data.") },
                null);
        }
        catch (Exception ex)
        {
            return (
                new[] { new TableInfoDto(tableName, "API Endpoint", 0, $"REST API connection failed: {ex.Message}") },
                null);
        }
    }

    public async Task<(IReadOnlyList<string> Fields, string? Error)> GetFieldsAsync(Datasource ds)
    {
        try
        {
            var apiResult = await _queryService.ExecuteRestApiAsync(ds);
            if (apiResult.Success && apiResult.Data.Count > 0)
            {
                var fields = apiResult.Data.First().Keys.ToList();
                if (fields.Count > 0) return (fields, null);
            }
            return (Array.Empty<string>(), apiResult.Error ?? "REST API returned no data.");
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
            var apiResult = await _queryService.ExecuteRestApiAsync(ds);
            if (apiResult.Success && apiResult.Data.Count > 0)
            {
                var firstRow = apiResult.Data.First();
                var columns = firstRow.Select(kv => new ColumnSchemaDto(
                        kv.Key,
                        DatasourceTypeJsonHelpers.InferJsonType(kv.Value),
                        string.Equals(kv.Key, "id", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var name = (ds.Name ?? "").Replace(" ", "_");
                return (new[] { new TableSchemaDto(name, "API Endpoint", columns) }, null);
            }
            return (Array.Empty<TableSchemaDto>(), apiResult.Error);
        }
        catch
        {
            return (Array.Empty<TableSchemaDto>(), null);
        }
    }
}
