namespace GatewayApp.Models;

public sealed class CaptchaChallenge
{
    public string CaptchaId { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;

    /// <summary>
    /// Plain-text CAPTCHA question (e.g. "5 + 3 = ?") used by the WPF client to render
    /// the challenge natively without relying on an HTML/SVG host.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
