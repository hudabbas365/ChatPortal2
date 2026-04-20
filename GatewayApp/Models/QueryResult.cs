namespace GatewayApp.Models;

public sealed class QueryResult
{
    public string Data { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public long ExecutionTimeMs { get; set; }
    public string DatasourceId { get; set; } = string.Empty;
}
