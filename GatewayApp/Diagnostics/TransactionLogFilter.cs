namespace GatewayApp.Diagnostics;

public sealed class TransactionLogFilter
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? DatasourceId { get; set; }
    public string? Status { get; set; }
}
