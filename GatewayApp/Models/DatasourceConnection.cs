namespace GatewayApp.Models;

public sealed class DatasourceConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "OnPremises";
    public string ConnectionString { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
