using System.Net;
using System.Net.Http;
using System.Security.Authentication;

namespace GatewayApp.Services;

public sealed class HttpsEnforcingHandler : DelegatingHandler
{
    public static HttpClient CreateSecureClient(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Only HTTPS endpoints are allowed.");
        }

        var handler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CheckCertificateRevocationList = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        var httpsHandler = new HttpsEnforcingHandler { InnerHandler = handler };
        return new HttpClient(httpsHandler) { BaseAddress = uri };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null || request.RequestUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("HTTP endpoints are blocked. Please use HTTPS.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
