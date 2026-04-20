namespace GatewayApp.Models;

public sealed class AuthResult
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expiry { get; set; }
    public string? UserId { get; set; }
    public string? OrganizationId { get; set; }
    public string? FullName { get; set; }
    public string? OrgName { get; set; }
    public string? Role { get; set; }
}
