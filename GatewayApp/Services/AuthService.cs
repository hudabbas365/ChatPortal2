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
    public string OrganizationId => _session.OrganizationId;

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        var payload = JsonSerializer.Serialize(new { username, password }, JsonDefaults.Options);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("/api/auth/login", content).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Authentication failed. Please verify your credentials.");
        }

        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var authResult = JsonSerializer.Deserialize<AuthResult>(responseBody, JsonDefaults.Options)
            ?? throw new InvalidOperationException("Authentication response was invalid.");

        _session.IsAuthenticated = true;
        _session.CurrentUser = username;
        _session.Token = authResult.Token;
        _session.OrganizationId = authResult.OrganizationId;
        _session.TokenExpiryUtc = authResult.Expiry;

        return authResult;
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
}
