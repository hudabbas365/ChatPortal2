using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIInsights.Services;

public interface IPayPalService
{
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string description, string returnUrl, string cancelUrl);
    Task<PayPalCaptureResult> CaptureOrderAsync(string orderId);
}

public class PayPalService : IPayPalService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    public PayPalService(IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _http = httpClientFactory.CreateClient("PayPal");
    }

    private string ClientId => _config["PayPal:ClientId"] ?? "";
    private string SecretKey => _config["PayPal:SecretKey"] ?? "";
    private bool IsSandbox => !string.Equals(_config["PayPal:Environment"], "live", StringComparison.OrdinalIgnoreCase);
    private string BaseUrl => IsSandbox
        ? "https://api-m.sandbox.paypal.com"
        : "https://api-m.paypal.com";

    private async Task<string> GetAccessTokenAsync()
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{SecretKey}"));
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString() ?? "";
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string description, string returnUrl, string cancelUrl)
    {
        var token = await GetAccessTokenAsync();
        var orderBody = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    description,
                    amount = new { currency_code = currency, value = amount.ToString("F2") }
                }
            },
            application_context = new
            {
                return_url = returnUrl,
                cancel_url = cancelUrl,
                brand_name = "AIInsights",
                user_action = "PAY_NOW"
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/checkout/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(orderBody), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return new PayPalOrderResult { Success = false, Error = $"PayPal error: {json}" };

        var doc = JsonDocument.Parse(json);
        var orderId = doc.RootElement.GetProperty("id").GetString() ?? "";
        var approveUrl = "";
        foreach (var link in doc.RootElement.GetProperty("links").EnumerateArray())
        {
            if (link.GetProperty("rel").GetString() == "approve")
            {
                approveUrl = link.GetProperty("href").GetString() ?? "";
                break;
            }
        }

        return new PayPalOrderResult { Success = true, OrderId = orderId, ApproveUrl = approveUrl };
    }

    public async Task<PayPalCaptureResult> CaptureOrderAsync(string orderId)
    {
        var token = await GetAccessTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/checkout/orders/{orderId}/capture");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return new PayPalCaptureResult { Success = false, Error = $"Capture failed: {json}" };

        var doc = JsonDocument.Parse(json);
        var status = doc.RootElement.GetProperty("status").GetString() ?? "";

        return new PayPalCaptureResult
        {
            Success = status == "COMPLETED",
            Status = status,
            OrderId = orderId,
            Error = status != "COMPLETED" ? $"Order status: {status}" : null
        };
    }
}

public class PayPalOrderResult
{
    public bool Success { get; set; }
    public string OrderId { get; set; } = "";
    public string ApproveUrl { get; set; } = "";
    public string? Error { get; set; }
}

public class PayPalCaptureResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string? Error { get; set; }
}
