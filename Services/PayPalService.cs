using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace AIInsights.Services;

public interface IPayPalService
{
    Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string description, string returnUrl, string cancelUrl);
    Task<PayPalCaptureResult> CaptureOrderAsync(string orderId);
    Task<PayPalProductResult> CreateProductAsync(string name, string description);
    Task<PayPalPlanResult> CreatePlanAsync(string productId, string planName, decimal monthlyPrice, string currency = "USD");
    Task<PayPalSubscriptionResult> CreateSubscriptionAsync(string planId, string returnUrl, string cancelUrl);
    Task<PayPalSubscriptionDetails?> GetSubscriptionDetailsAsync(string subscriptionId);
    Task<bool> CancelSubscriptionAsync(string subscriptionId, string reason);

    // Webhook signature verification (PayPal v1/notifications/verify-webhook-signature)
    Task<bool> VerifyWebhookSignatureAsync(IHeaderDictionary headers, string rawBody);

    // Reuses or lazily creates the catalog Product + Plan IDs so we don't spam
    // PayPal with duplicate product/plan objects on every checkout.
    Task<PayPalPlanResult> EnsureSubscriptionPlanAsync(string planKey, decimal monthlyPrice);

    // Reverse lookup: given a PayPal plan_id, return the tier name
    // ("Professional" / "Enterprise") it was created for. Used to recover the
    // correct PlanType when activating a subscription where the client-supplied
    // planKey is missing or wrong.
    bool TryResolvePlanKeyFromPlanId(string planId, out string? planKey);
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
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("PayPal ClientId or SecretKey is not configured.");

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{SecretKey}"));
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

        var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"PayPal auth failed ({(int)response.StatusCode}). " +
                $"Endpoint: {BaseUrl} (Sandbox={IsSandbox}). Response: {body}");

        var doc = JsonDocument.Parse(body);
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

    // ── Subscriptions API ──────────────────────────────────────────

    public async Task<PayPalProductResult> CreateProductAsync(string name, string description)
    {
        var token = await GetAccessTokenAsync();
        var body = new { name, description, type = "SERVICE", category = "SOFTWARE" };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/catalogs/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return new PayPalProductResult { Success = false, Error = $"Create product failed: {json}" };

        var doc = JsonDocument.Parse(json);
        return new PayPalProductResult
        {
            Success = true,
            ProductId = doc.RootElement.GetProperty("id").GetString() ?? ""
        };
    }

    public async Task<PayPalPlanResult> CreatePlanAsync(string productId, string planName, decimal monthlyPrice, string currency = "USD")
    {
        var token = await GetAccessTokenAsync();
        var body = new
        {
            product_id = productId,
            name = planName,
            description = $"{planName} — Monthly recurring subscription",
            status = "ACTIVE",
            billing_cycles = new[]
            {
                new
                {
                    frequency = new { interval_unit = "MONTH", interval_count = 1 },
                    tenure_type = "REGULAR",
                    sequence = 1,
                    total_cycles = 0, // infinite
                    pricing_scheme = new
                    {
                        fixed_price = new { value = monthlyPrice.ToString("F2"), currency_code = currency }
                    }
                }
            },
            payment_preferences = new
            {
                auto_bill_outstanding = true,
                payment_failure_threshold = 3
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/billing/plans");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return new PayPalPlanResult { Success = false, Error = $"Create plan failed: {json}" };

        var doc = JsonDocument.Parse(json);
        return new PayPalPlanResult
        {
            Success = true,
            PlanId = doc.RootElement.GetProperty("id").GetString() ?? ""
        };
    }

    public async Task<PayPalSubscriptionResult> CreateSubscriptionAsync(string planId, string returnUrl, string cancelUrl)
    {
        var token = await GetAccessTokenAsync();
        var body = new
        {
            plan_id = planId,
            application_context = new
            {
                brand_name = "AIInsights",
                user_action = "SUBSCRIBE_NOW",
                return_url = returnUrl,
                cancel_url = cancelUrl
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/billing/subscriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return new PayPalSubscriptionResult { Success = false, Error = $"Create subscription failed: {json}" };

        var doc = JsonDocument.Parse(json);
        var subscriptionId = doc.RootElement.GetProperty("id").GetString() ?? "";
        var approveUrl = "";
        foreach (var link in doc.RootElement.GetProperty("links").EnumerateArray())
        {
            if (link.GetProperty("rel").GetString() == "approve")
            {
                approveUrl = link.GetProperty("href").GetString() ?? "";
                break;
            }
        }

        return new PayPalSubscriptionResult
        {
            Success = true,
            SubscriptionId = subscriptionId,
            ApproveUrl = approveUrl
        };
    }

    public async Task<PayPalSubscriptionDetails?> GetSubscriptionDetailsAsync(string subscriptionId)
    {
        var token = await GetAccessTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v1/billing/subscriptions/{subscriptionId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return null;

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        DateTime? nextBilling = null;
        if (root.TryGetProperty("billing_info", out var billing) &&
            billing.TryGetProperty("next_billing_time", out var nbt))
        {
            if (DateTime.TryParse(nbt.GetString(), out var dt))
                nextBilling = dt;
        }

        return new PayPalSubscriptionDetails
        {
            SubscriptionId = root.GetProperty("id").GetString() ?? "",
            Status = root.GetProperty("status").GetString() ?? "",
            PlanId = root.GetProperty("plan_id").GetString() ?? "",
            StartTime = root.TryGetProperty("start_time", out var st) ? st.GetString() : null,
            NextBillingTime = nextBilling
        };
    }

    public async Task<bool> CancelSubscriptionAsync(string subscriptionId, string reason)
    {
        var token = await GetAccessTokenAsync();
        var body = new { reason };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/billing/subscriptions/{subscriptionId}/cancel");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        return response.IsSuccessStatusCode || (int)response.StatusCode == 204;
    }

    // ── Webhook signature verification ─────────────────────────────
    // POSTs to /v1/notifications/verify-webhook-signature with the 5 PayPal
    // transmission headers + the configured WebhookId + the parsed event body.
    // Returns true only when PayPal responds with verification_status=="SUCCESS".
    public async Task<bool> VerifyWebhookSignatureAsync(IHeaderDictionary headers, string rawBody)
    {
        var webhookId = _config["PayPal:WebhookId"];
        if (string.IsNullOrWhiteSpace(webhookId)) return false;
        if (string.IsNullOrWhiteSpace(rawBody)) return false;

        string H(string k) => headers.TryGetValue(k, out var v) ? v.ToString() : "";
        var authAlgo = H("PAYPAL-AUTH-ALGO");
        var certUrl = H("PAYPAL-CERT-URL");
        var transmissionId = H("PAYPAL-TRANSMISSION-ID");
        var transmissionSig = H("PAYPAL-TRANSMISSION-SIG");
        var transmissionTime = H("PAYPAL-TRANSMISSION-TIME");

        if (string.IsNullOrEmpty(authAlgo) || string.IsNullOrEmpty(certUrl) ||
            string.IsNullOrEmpty(transmissionId) || string.IsNullOrEmpty(transmissionSig) ||
            string.IsNullOrEmpty(transmissionTime))
            return false;

        try
        {
            var token = await GetAccessTokenAsync();
            using var eventDoc = JsonDocument.Parse(rawBody);
            var verifyBody = new
            {
                auth_algo = authAlgo,
                cert_url = certUrl,
                transmission_id = transmissionId,
                transmission_sig = transmissionSig,
                transmission_time = transmissionTime,
                webhook_id = webhookId,
                webhook_event = eventDoc.RootElement
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/notifications/verify-webhook-signature");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(verifyBody), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.TryGetProperty("verification_status", out var s) ? s.GetString() : null;
            return string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // ── Catalog Product + Plan caching ─────────────────────────────
    // PayPal lets us reuse a single Product + Plan combo across every
    // subscription checkout, instead of creating fresh ones each click.
    // Strategy:
    //  1. Honour static IDs from configuration (PayPal:ProductId,
    //     PayPal:ProPlanId, PayPal:EnterprisePlanId) when present.
    //  2. Otherwise lazily create-once and cache in-memory for the
    //     lifetime of the process.
    private static string? _cachedProductId;
    private static readonly ConcurrentDictionary<string, string> _planCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim _catalogLock = new(1, 1);

    public async Task<PayPalPlanResult> EnsureSubscriptionPlanAsync(string planKey, decimal monthlyPrice)
    {
        // 1. Configured plan id wins.
        var configKey = $"PayPal:{planKey}PlanId";
        var configured = _config[configKey];
        if (!string.IsNullOrWhiteSpace(configured))
            return new PayPalPlanResult { Success = true, PlanId = configured };

        // 2. In-memory cache.
        var cacheKey = $"{planKey}|{monthlyPrice:F2}";
        if (_planCache.TryGetValue(cacheKey, out var cachedPlanId))
            return new PayPalPlanResult { Success = true, PlanId = cachedPlanId };

        await _catalogLock.WaitAsync();
        try
        {
            if (_planCache.TryGetValue(cacheKey, out cachedPlanId))
                return new PayPalPlanResult { Success = true, PlanId = cachedPlanId };

            // Resolve product id (config → cache → create).
            var productId = _config["PayPal:ProductId"];
            if (string.IsNullOrWhiteSpace(productId)) productId = _cachedProductId;
            if (string.IsNullOrWhiteSpace(productId))
            {
                var product = await CreateProductAsync("AIInsights Subscription", "AIInsights monthly plan subscription");
                if (!product.Success) return new PayPalPlanResult { Success = false, Error = product.Error };
                productId = product.ProductId;
                _cachedProductId = productId;
            }

            var plan = await CreatePlanAsync(productId!, $"AIInsights {planKey} Monthly", monthlyPrice);
            if (!plan.Success) return plan;

            _planCache[cacheKey] = plan.PlanId;
            return plan;
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public bool TryResolvePlanKeyFromPlanId(string planId, out string? planKey)
    {
        planKey = null;
        if (string.IsNullOrWhiteSpace(planId)) return false;

        // 1. Check configured plan ids first (PayPal:ProfessionalPlanId, PayPal:EnterprisePlanId).
        foreach (var tier in new[] { "Professional", "Enterprise" })
        {
            var cfg = _config[$"PayPal:{tier}PlanId"];
            if (!string.IsNullOrWhiteSpace(cfg) && string.Equals(cfg, planId, StringComparison.OrdinalIgnoreCase))
            {
                planKey = tier;
                return true;
            }
        }

        // 2. Reverse-scan the in-memory cache (keys look like "Professional|25.00").
        foreach (var kv in _planCache)
        {
            if (string.Equals(kv.Value, planId, StringComparison.OrdinalIgnoreCase))
            {
                var sep = kv.Key.IndexOf('|');
                planKey = sep > 0 ? kv.Key[..sep] : kv.Key;
                return true;
            }
        }
        return false;
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

public class PayPalProductResult
{
    public bool Success { get; set; }
    public string ProductId { get; set; } = "";
    public string? Error { get; set; }
}

public class PayPalPlanResult
{
    public bool Success { get; set; }
    public string PlanId { get; set; } = "";
    public string? Error { get; set; }
}

public class PayPalSubscriptionResult
{
    public bool Success { get; set; }
    public string SubscriptionId { get; set; } = "";
    public string ApproveUrl { get; set; } = "";
    public string? Error { get; set; }
}

public class PayPalSubscriptionDetails
{
    public string SubscriptionId { get; set; } = "";
    public string Status { get; set; } = "";
    public string PlanId { get; set; } = "";
    public string? StartTime { get; set; }
    public DateTime? NextBillingTime { get; set; }
}
