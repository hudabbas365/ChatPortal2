namespace GatewayApp.Models;

public sealed class GatewaySettings
{
    public string GatewayId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string GatewayName { get; set; } = "Default Gateway";
    public bool IsAIGatewayEnabled { get; set; }
    public string ReleaseVersion { get; set; } = string.Empty;
    public string ReleaseDate { get; set; } = string.Empty;
}
