namespace GatewayApp.Models;

public sealed class CaptchaChallenge
{
    public string CaptchaId { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
}
