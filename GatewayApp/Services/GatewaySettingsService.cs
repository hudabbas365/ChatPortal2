using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GatewayApp.Models;

namespace GatewayApp.Services;

public sealed class GatewaySettingsService
{
    private readonly FileEncryptionService _encryptionService;
    private readonly string _configPath;
    private readonly HttpClient _httpClient;

    public GatewaySettingsService(FileEncryptionService encryptionService, string apiBaseUrl = "https://localhost:5001")
    {
        _encryptionService = encryptionService;
        _httpClient = HttpsEnforcingHandler.CreateSecureClient(apiBaseUrl);

        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIInsights365Gateway");
        Directory.CreateDirectory(dataDir);
        _configPath = Path.Combine(dataDir, "gateway-config.json");
    }

    public async Task<GatewaySettings?> LoadAsync()
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }

        var encrypted = await File.ReadAllTextAsync(_configPath).ConfigureAwait(false);
        var json = _encryptionService.Decrypt(encrypted);
        return JsonSerializer.Deserialize<GatewaySettings>(json, JsonDefaults.Options);
    }

    public async Task SaveAsync(GatewaySettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var encrypted = _encryptionService.Encrypt(json);
        await File.WriteAllTextAsync(_configPath, encrypted).ConfigureAwait(false);
    }

    public async Task RegisterGatewayAsync(string? orgId, string gatewayName, string version, string releaseDate, string token)
    {
        if (string.IsNullOrWhiteSpace(orgId) || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        object organizationId = int.TryParse(orgId, out var orgNumber) ? orgNumber : orgId;
        var payload = new
        {
            OrganizationId = organizationId,
            GatewayName = gatewayName,
            Version = version,
            ReleaseDate = releaseDate
        };

        var body = JsonSerializer.Serialize(payload, JsonDefaults.Options);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/gateway/register")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"Gateway registration failed: {(int)response.StatusCode} {response.ReasonPhrase}. {errorBody}".Trim());
        }
    }

    public string GetConfigPath() => _configPath;
}
