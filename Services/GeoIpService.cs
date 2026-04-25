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
        if (!System.Net.IPAddress.TryParse(ip, out var address))
            return true; // unparseable — skip lookup

        if (System.Net.IPAddress.IsLoopback(address))
            return true;

        // Normalise IPv4-mapped IPv6 (e.g. ::ffff:10.0.0.1)
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return
                b[0] == 10 ||                                         // 10.0.0.0/8
                b[0] == 127 ||                                        // 127.0.0.0/8
                (b[0] == 169 && b[1] == 254) ||                      // 169.254.0.0/16 link-local
                (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||         // 172.16.0.0/12
                (b[0] == 192 && b[1] == 168) ||                      // 192.168.0.0/16
                (b[0] == 100 && (b[1] & 0b1100_0000) == 64);         // 100.64.0.0/10 shared
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            // fc00::/7 ULA, fe80::/10 link-local
            return (b[0] & 0xFE) == 0xFC || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80);
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
