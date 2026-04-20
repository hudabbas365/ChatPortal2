namespace GatewayApp.Models;

public sealed class UserSession
{
    public bool IsAuthenticated { get; set; }
    public string CurrentUser { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime TokenExpiryUtc { get; set; }
    public string OrganizationId { get; set; } = string.Empty;
}
