using AIInsights.Models;
using Microsoft.Extensions.Caching.Memory;

namespace AIInsights.Services;

public class GeoIpService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<GeoIpService> _logger;

    public GeoIpService(IHttpClientFactory httpClientFactory, IMemoryCache cache, IConfiguration config, ILogger<GeoIpService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    public async Task<(string? Country, string? City)> LookupAsync(string? ip)
    {
        if (!_config.GetValue<bool>("GeoIp:Enabled", true))
            return (null, null);

        if (string.IsNullOrWhiteSpace(ip))
            return (null, null);

        // Skip private/loopback ranges
        if (IsPrivateIp(ip))
            return (null, null);

        var cacheKey = $"geoip:{ip}";
        if (_cache.TryGetValue(cacheKey, out (string? Country, string? City) cached))
            return cached;

        try
        {
            var client = _httpClientFactory.CreateClient("geoip");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            var response = await client.GetAsync($"https://ipapi.co/{ip}/json/", cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _cache.Set(cacheKey, ((string?)null, (string?)null), TimeSpan.FromHours(1));
                return (null, null);
            }

            var json = await response.Content.ReadFromJsonAsync<IpapiResponse>();
            var result = (json?.CountryCode, json?.City);
            _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeoIP lookup failed for IP {Ip}", ip);
            _cache.Set(cacheKey, ((string?)null, (string?)null), TimeSpan.FromHours(1));
            return (null, null);
        }
    }

    private static bool IsPrivateIp(string ip)
    {
        if (ip == "::1" || ip == "127.0.0.1") return true;
        if (ip.StartsWith("10.")) return true;
        if (ip.StartsWith("192.168.")) return true;
        if (ip.StartsWith("172."))
        {
            var parts = ip.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var second))
                return second >= 16 && second <= 31;
        }
        return false;
    }

    private class IpapiResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("city")]
        public string? City { get; set; }
    }
}
