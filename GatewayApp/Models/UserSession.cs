namespace GatewayApp.Models;

public sealed class UserSession
{
    public bool IsAuthenticated { get; set; }
    public string CurrentUser { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
    public string? FullName { get; set; }
    public string? OrgName { get; set; }
    public DateTime TokenExpiryUtc { get; set; }
}
