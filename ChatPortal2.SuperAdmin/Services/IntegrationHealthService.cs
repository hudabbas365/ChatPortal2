using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Net.Sockets;

namespace AIInsights.SuperAdmin.Services;

public class IntegrationHealthService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IntegrationHealthService> _logger;
    private readonly Dictionary<string, string> _lastKnownStatus = new();

    public IntegrationHealthService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<IntegrationHealthService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunChecksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IntegrationHealthService check cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task RunChecksAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var checks = new List<(string Provider, Func<CancellationToken, Task<(string Status, int LatencyMs, string? Error)>> Check)>
        {
            ("Cohere", CheckCohereAsync),
            ("Smtp", CheckSmtpAsync),
            ("PayPal", CheckPayPalAsync)
        };

        foreach (var (provider, check) in checks)
        {
            var (status, latency, error) = await check(ct);

            // Transition Up → Down alert
            if (_lastKnownStatus.TryGetValue(provider, out var prev) && prev == "Up" && status == "Down")
            {
                db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "Integration.Down",
                    Description = $"Integration '{provider}' transitioned from Up to Down. Error: {error}",
                    UserId = "",
                    CreatedAt = DateTime.UtcNow
                });

                await SendAlertEmailAsync(provider, error, scope.ServiceProvider);
            }

            _lastKnownStatus[provider] = status;

            db.IntegrationHealthChecks.Add(new IntegrationHealthCheck
            {
                Provider = provider,
                Status = status,
                LatencyMs = latency,
                Error = error,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);

        // Purge old records (keep 48h)
        var cutoff = DateTime.UtcNow.AddHours(-48);
        await db.IntegrationHealthChecks
            .Where(h => h.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private async Task<(string Status, int LatencyMs, string? Error)> CheckCohereAsync(CancellationToken ct)
    {
        var apiKey = _config["Cohere:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return ("Unknown", 0, "API key not configured");

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            var body = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    model = _config["Cohere:Model"] ?? "command-r",
                    message = "Hi",
                    max_tokens = 1
                }),
                System.Text.Encoding.UTF8,
                "application/json");

            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.cohere.ai/v1/chat");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = body;

            var resp = await client.SendAsync(req, cts.Token);
            sw.Stop();
            return resp.IsSuccessStatusCode
                ? ("Up", (int)sw.ElapsedMilliseconds, null)
                : ("Down", (int)sw.ElapsedMilliseconds, $"HTTP {(int)resp.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return ("Down", (int)sw.ElapsedMilliseconds, "Timeout");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ("Down", (int)sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private async Task<(string Status, int LatencyMs, string? Error)> CheckSmtpAsync(CancellationToken ct)
    {
        var host = _config["Smtp:Host"];
        var portStr = _config["Smtp:Port"];
        if (string.IsNullOrWhiteSpace(host))
            return ("Unknown", 0, "SMTP host not configured");

        var port = int.TryParse(portStr, out var p) ? p : 587;
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, cts.Token);
            sw.Stop();

            if (!tcp.Connected)
                return ("Down", (int)sw.ElapsedMilliseconds, "Could not connect");

            return ("Up", (int)sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return ("Down", (int)sw.ElapsedMilliseconds, "Timeout");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ("Down", (int)sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private async Task<(string Status, int LatencyMs, string? Error)> CheckPayPalAsync(CancellationToken ct)
    {
        var clientId = _config["PayPal:ClientId"];
        var secret = _config["PayPal:SecretKey"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            return ("Unknown", 0, "PayPal credentials not configured");

        var env = _config["PayPal:Environment"] ?? "sandbox";
        var baseUrl = env.ToLower() == "live"
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{secret}"));
            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/oauth2/token");
            req.Headers.Add("Authorization", $"Basic {credentials}");
            req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

            var resp = await client.SendAsync(req, cts.Token);
            sw.Stop();
            return resp.IsSuccessStatusCode
                ? ("Up", (int)sw.ElapsedMilliseconds, null)
                : ("Down", (int)sw.ElapsedMilliseconds, $"HTTP {(int)resp.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return ("Down", (int)sw.ElapsedMilliseconds, "Timeout");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ("Down", (int)sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private async Task SendAlertEmailAsync(string provider, string? error, IServiceProvider services)
    {
        var alertEmails = _config.GetSection("SuperAdmin:AlertEmails").Get<string[]>();
        if (alertEmails == null || alertEmails.Length == 0) return;

        // Use SmtpClient directly since we're in SuperAdmin process
        var host = _config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host)) return;

        try
        {
            var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
            var from = _config["Smtp:From"] ?? "noreply@aiinsights365.net";
            var username = _config["Smtp:Username"];
            var password = _config["Smtp:Password"];
            var enableSsl = _config.GetValue<bool>("Smtp:EnableSsl", true);

            using var smtp = new System.Net.Mail.SmtpClient(host, port);
            smtp.EnableSsl = enableSsl;
            if (!string.IsNullOrWhiteSpace(username))
                smtp.Credentials = new System.Net.NetworkCredential(username, password);

            foreach (var email in alertEmails)
            {
                var msg = new System.Net.Mail.MailMessage(from, email,
                    $"[AIInsights365] Integration Alert: {provider} is DOWN",
                    $"The {provider} integration has gone DOWN at {DateTime.UtcNow:O}.\n\nError: {error ?? "Unknown"}\n\nPlease investigate.");
                await smtp.SendMailAsync(msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send integration-down alert email for provider {Provider}", provider);
        }
    }
}
