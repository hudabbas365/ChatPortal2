using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GatewayApp.Models;

namespace GatewayApp.Services;

public sealed class AuthService
{
    private readonly HttpClient _httpClient;
    private readonly UserSession _session = new();

    public AuthService(string apiBaseUrl)
    {
        _httpClient = HttpsEnforcingHandler.CreateSecureClient(apiBaseUrl);
    }

    public bool IsAuthenticated => _session.IsAuthenticated;
    public string CurrentUser => _session.CurrentUser;
    public string Token => _session.Token;
    public string? OrganizationId => _session.OrganizationId;
    public string? FullName => _session.FullName;
    public string? OrgName => _session.OrgName;

    public async Task<CaptchaChallenge> GetCaptchaAsync()
    {
        using var response = await _httpClient.GetAsync("/api/auth/captcha").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<CaptchaChallenge>(json, JsonDefaults.Options)
            ?? throw new InvalidOperationException("Invalid CAPTCHA response.");
    }

    public async Task<AuthResult> LoginAsync(string email, string password, string captchaId, string captchaAnswer)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Email = email,
            Password = password,
            CaptchaId = captchaId,
            CaptchaAnswer = captchaAnswer
        }, JsonDefaults.Options);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("/api/auth/login", content).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractError(body));
        }

        using var responseDoc = JsonDocument.Parse(body);
        var root = responseDoc.RootElement;

        var token = GetCaseInsensitiveString(root, "token");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Server returned an empty token.");
        }

        string? fullName = null;
        string? role = null;
        string? orgName = null;
        string? userId = null;
        string? orgIdStr = null;

        if (TryGetCaseInsensitiveProperty(root, "user", out var userEl))
        {
            fullName = GetCaseInsensitiveString(userEl, "fullName");
            role = GetCaseInsensitiveString(userEl, "role");
            orgName = GetCaseInsensitiveString(userEl, "orgName");
            userId = GetCaseInsensitiveString(userEl, "id");

            if (TryGetCaseInsensitiveProperty(userEl, "organizationId", out var orgEl))
            {
                orgIdStr = orgEl.ValueKind switch
                {
                    JsonValueKind.Number => orgEl.TryGetInt32(out var value) ? value.ToString() : orgEl.GetRawText(),
                    JsonValueKind.String => orgEl.GetString(),
                    _ => null
                };
            }
        }

        _session.IsAuthenticated = true;
        _session.CurrentUser = email;
        _session.Token = token;
        _session.OrganizationId = orgIdStr;
        _session.FullName = fullName;
        _session.OrgName = orgName;
        _session.TokenExpiryUtc = DateTime.UtcNow.AddHours(24);

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return new AuthResult
        {
            Token = token,
            Expiry = _session.TokenExpiryUtc,
            UserId = userId,
            OrganizationId = orgIdStr,
            FullName = fullName,
            OrgName = orgName,
            Role = role
        };
    }

    public async Task<bool> RefreshTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(_session.Token))
        {
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Token);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<AuthResult>(body, JsonDefaults.Options);
        if (result is null || string.IsNullOrWhiteSpace(result.Token))
        {
            return false;
        }

        _session.Token = result.Token;
        _session.TokenExpiryUtc = result.Expiry;
        return true;
    }

    public void ApplyAuthorization(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_session.Token))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Token);
    }

    public void Logout()
    {
        _session.IsAuthenticated = false;
        _session.Token = string.Empty;
        _session.CurrentUser = string.Empty;
        _session.OrganizationId = null;
        _session.FullName = null;
        _session.OrgName = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    private static string ExtractError(string body)
    {
        const string fallback = "Authentication failed. Please check your credentials.";

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var message = GetCaseInsensitiveString(root, "message");
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            var error = GetCaseInsensitiveString(root, "error");
            if (!string.IsNullOrWhiteSpace(error))
            {
                return error;
            }
        }
        catch
        {
            // Ignore parse failures and use fallback message.
        }

        return fallback;
    }

    private static string? GetCaseInsensitiveString(JsonElement element, string propertyName)
    {
        if (!TryGetCaseInsensitiveProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private static bool TryGetCaseInsensitiveProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
