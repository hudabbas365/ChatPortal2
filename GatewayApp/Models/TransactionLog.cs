namespace GatewayApp.Models;

public sealed class TransactionLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Query { get; set; } = string.Empty;
    public string DatasourceId { get; set; } = string.Empty;
    public string DatasourceName { get; set; } = string.Empty;
    public string Status { get; set; } = "Success";
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}
