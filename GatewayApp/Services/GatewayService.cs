using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GatewayApp.Models;

namespace GatewayApp.Services;

public sealed class GatewayService
{
    private readonly HttpClient _httpClient;
    private readonly AuthService _authService;
    private readonly DiagnosticsService _diagnosticsService;

    public GatewayService(string apiBaseUrl, AuthService authService, DiagnosticsService diagnosticsService)
    {
        _httpClient = HttpsEnforcingHandler.CreateSecureClient(apiBaseUrl);
        _authService = authService;
        _diagnosticsService = diagnosticsService;
    }

    public async Task<GatewayResponse> ForwardQueryAsync(string query, string datasourceId)
    {
        var delays = new[] { 250, 500, 1000 };
        Exception? lastException = null;

        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                var payload = JsonSerializer.Serialize(new { query, datasourceId }, JsonDefaults.Options);
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/gateway/query")
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };

                _authService.ApplyAuthorization(request);
                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<QueryResult>(json, JsonDefaults.Options) ?? new QueryResult();

                _diagnosticsService.LogTransaction(new TransactionLog
                {
                    Query = query,
                    DatasourceId = datasourceId,
                    DatasourceName = datasourceId,
                    DurationMs = timer.ElapsedMilliseconds,
                    Status = "Success"
                });

                return new GatewayResponse { Success = true, Result = result, Message = "Query forwarded successfully." };
            }
            catch (Exception ex)
            {
                lastException = ex;
                _diagnosticsService.LogTransaction(new TransactionLog
                {
                    Query = query,
                    DatasourceId = datasourceId,
                    DatasourceName = datasourceId,
                    DurationMs = timer.ElapsedMilliseconds,
                    Status = "Failed",
                    ErrorMessage = ex.Message
                });

                if (attempt < delays.Length - 1)
                {
                    await Task.Delay(delays[attempt]).ConfigureAwait(false);
                }
            }
        }

        return new GatewayResponse
        {
            Success = false,
            Message = $"Failed after 3 attempts. {lastException?.Message}"
        };
    }

    public async Task<GatewaySettings> RegisterGatewayAsync(GatewaySettings settings)
    {
        var payload = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/gateway/register")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        _authService.ApplyAuthorization(request);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<GatewaySettings>(json, JsonDefaults.Options) ?? settings;
    }

    public async Task<bool> SyncSettingsAsync(GatewaySettings settings)
    {
        var payload = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/gateway/settings")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        _authService.ApplyAuthorization(request);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
}
