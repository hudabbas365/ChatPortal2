namespace GatewayApp.Models;

public sealed class GatewayResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public QueryResult? Result { get; set; }
}
