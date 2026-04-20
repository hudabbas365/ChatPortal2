using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GatewayApp.Models;

namespace GatewayApp.Services;

public sealed class DatasourceService
{
    private readonly HttpClient _httpClient;
    private readonly AuthService _authService;

    public DatasourceService(string apiBaseUrl, AuthService authService)
    {
        _httpClient = HttpsEnforcingHandler.CreateSecureClient(apiBaseUrl);
        _authService = authService;
    }

    public async Task<List<DatasourceConnection>> GetDatasourcesAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/gateway/datasources");
        _authService.ApplyAuthorization(request);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<DatasourceConnection>>(json, JsonDefaults.Options) ?? new List<DatasourceConnection>();
    }

    public async Task<bool> AddDatasourceAsync(DatasourceConnection conn)
    {
        var payload = JsonSerializer.Serialize(conn, JsonDefaults.Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/gateway/datasources")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        _authService.ApplyAuthorization(request);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<QueryResult> ExecuteQueryAsync(string datasourceId, string query)
    {
        var payload = JsonSerializer.Serialize(new { datasourceId, query }, JsonDefaults.Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/gateway/query")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        _authService.ApplyAuthorization(request);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<QueryResult>(json, JsonDefaults.Options)
               ?? new QueryResult { DatasourceId = datasourceId };
    }

    public Task<bool> TestConnectionAsync(DatasourceConnection connection)
    {
        var isValid = !string.IsNullOrWhiteSpace(connection.Name)
                      && !string.IsNullOrWhiteSpace(connection.ConnectionString);
        return Task.FromResult(isValid);
    }
}
