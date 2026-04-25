using System.Data;
using AIInsights.Models;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.Identity.Client;

namespace AIInsights.Services;

public interface IPowerBiService
{
    Task<QueryExecutionResult> ExecuteDaxAsync(Datasource ds, string daxQuery, int maxRows = 1000);
    Task<QueryExecutionResult> ExecuteDmvAsync(Datasource ds, string dmvQuery, int maxRows = 5000);
    Task<(bool Success, string? Error)> TestCredentialsAsync(string tenantId, string clientId, string clientSecret, string xmlaEndpoint, string? catalog = null);
}

public class PowerBiService : IPowerBiService
{
    private readonly IEncryptionService _encryption;
    private readonly ILogger<PowerBiService> _logger;

    public PowerBiService(IEncryptionService encryption, ILogger<PowerBiService> logger)
    {
        _encryption = encryption;
        _logger = logger;
    }

    public Task<QueryExecutionResult> ExecuteDaxAsync(Datasource ds, string daxQuery, int maxRows = 1000)
        => ExecuteInternalAsync(ds, daxQuery, maxRows);

    public Task<QueryExecutionResult> ExecuteDmvAsync(Datasource ds, string dmvQuery, int maxRows = 5000)
        => ExecuteInternalAsync(ds, dmvQuery, maxRows);

    private async Task<QueryExecutionResult> ExecuteInternalAsync(Datasource ds, string query, int maxRows)
    {
        AdomdConnection? conn = null;
        try
        {
            var connStr = await BuildConnectionStringAsync(ds);
            conn = new AdomdConnection(connStr);
            await Task.Run(() => conn.Open());

            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            cmd.CommandTimeout = 60;

            using var reader = cmd.ExecuteReader(CommandBehavior.SingleResult);
            var results = new List<Dictionary<string, object>>();
            var fieldCount = reader.FieldCount;
            var columns = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                columns[i] = reader.GetName(i);

            while (reader.Read() && results.Count < maxRows)
            {
                var row = new Dictionary<string, object>(fieldCount);
                for (int i = 0; i < fieldCount; i++)
                {
                    var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[columns[i]] = val ?? (object)"NULL";
                }
                results.Add(row);
            }

            return new QueryExecutionResult
            {
                Success = true,
                Data = results,
                RowCount = results.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Power BI query execution failed");
            return new QueryExecutionResult
            {
                Success = false,
                Error = $"Power BI query execution failed: {ex.Message}"
            };
        }
        finally
        {
            if (conn != null)
            {
                conn.Close();
                conn.Dispose();
            }
        }
    }

    private async Task<string> BuildConnectionStringAsync(Datasource ds)
    {
        var xmlaEndpoint = _encryption.Decrypt(ds.XmlaEndpoint ?? "");
        var catalog = _encryption.Decrypt(ds.ConnectionString ?? "");
        var tenantId = _encryption.Decrypt(ds.MicrosoftAccountTenantId ?? "");
        var clientId = _encryption.Decrypt(ds.DbUser ?? "");
        var clientSecret = _encryption.Decrypt(ds.DbPassword ?? "");

        var accessToken = await GetAccessTokenAsync(tenantId, clientId, clientSecret);

        return $"Data Source={xmlaEndpoint};Password={accessToken};Catalog={catalog};";
    }

    private static async Task<string> GetAccessTokenAsync(string tenantId, string clientId, string clientSecret)
    {
        var authority = $"https://login.microsoftonline.com/{tenantId}";
        var app = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(new Uri(authority))
            .Build();

        var scopes = new[] { "https://analysis.windows.net/powerbi/api/.default" };
        var result = await app.AcquireTokenForClient(scopes).ExecuteAsync();
        return result.AccessToken;
    }

    public async Task<(bool Success, string? Error)> TestCredentialsAsync(string tenantId, string clientId, string clientSecret, string xmlaEndpoint, string? catalog = null)
    {
        if (string.IsNullOrWhiteSpace(xmlaEndpoint))
            return (false, "XMLA Endpoint is required for Power BI datasources.");
        if (string.IsNullOrWhiteSpace(tenantId))
            return (false, "Tenant ID is required for Power BI datasources.");
        if (string.IsNullOrWhiteSpace(clientId))
            return (false, "Client ID is required for Power BI datasources.");
        if (string.IsNullOrWhiteSpace(clientSecret))
            return (false, "Client Secret is required for Power BI datasources.");

        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(tenantId, clientId, clientSecret);
        }
        catch (MsalServiceException ex)
        {
            _logger.LogWarning(ex, "Power BI credential validation failed (MSAL service error)");
            return (false, $"Authentication failed: {ex.Message}");
        }
        catch (MsalException ex)
        {
            _logger.LogWarning(ex, "Power BI credential validation failed (MSAL error)");
            return (false, $"Authentication failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Power BI credential validation failed");
            return (false, $"Authentication failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(accessToken))
            return (false, "Authentication failed: empty access token returned.");

        // If a catalog is provided, also probe the XMLA endpoint with the acquired token
        // so we validate that the endpoint and dataset are reachable with these credentials.
        if (!string.IsNullOrWhiteSpace(catalog))
        {
            AdomdConnection? probe = null;
            try
            {
                var connStr = $"Data Source={xmlaEndpoint};Password={accessToken};Catalog={catalog};";
                probe = new AdomdConnection(connStr);
                await Task.Run(() => probe.Open());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Power BI XMLA probe failed");
                return (false, $"XMLA endpoint check failed: {ex.Message}");
            }
            finally
            {
                if (probe != null) { try { probe.Close(); } catch { } probe.Dispose(); }
            }
        }

        return (true, null);
    }
}
