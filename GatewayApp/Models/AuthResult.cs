namespace GatewayApp.Models;

public sealed class AuthResult
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expiry { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
}
