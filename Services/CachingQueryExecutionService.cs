using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AIInsights.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace AIInsights.Services;

/// <summary>
/// Allows callers (e.g. DatasourceController) to invalidate all cached
/// query results for a datasource when its connection or tables change.
/// </summary>
public interface IQueryCacheInvalidator
{
    void InvalidateDatasource(int datasourceId);
}

/// <summary>
/// Decorator over <see cref="IQueryExecutionService"/> that caches read-only
/// query results and REST API payloads in memory for a short TTL. Write
/// guards and permission checks still run upstream in the controllers, so
/// caching is safe. Per-datasource cache entries are bound to a cancellation
/// token so a single invalidation flushes every query for that datasource.
/// </summary>
public class CachingQueryExecutionService : IQueryExecutionService, IQueryCacheInvalidator
{
    private readonly IQueryExecutionService _inner;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachingQueryExecutionService> _logger;

    private static readonly TimeSpan SqlTtl    = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SchemaTtl = TimeSpan.FromMinutes(15); // schema changes rarely
    private static readonly TimeSpan RestTtl   = TimeSpan.FromSeconds(30);
    private const long MaxResultRowsToCache    = 5_000; // avoid caching very large result sets

    // One CancellationTokenSource per datasource id — cancelling it removes
    // every cache entry registered against it in a single O(1) operation.
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _tokens = new();

    public CachingQueryExecutionService(
        IQueryExecutionService inner,
        IMemoryCache cache,
        ILogger<CachingQueryExecutionService> logger)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
    }

    public async Task<QueryExecutionResult> ExecuteReadOnlyAsync(Datasource ds, string sql, int maxRows = 1000)
    {
        if (ds == null) return await _inner.ExecuteReadOnlyAsync(ds!, sql, maxRows);

        var key = BuildSqlKey(ds.Id, sql, maxRows);
        if (_cache.TryGetValue<QueryExecutionResult>(key, out var hit) && hit != null)
        {
            _logger.LogDebug("Query cache HIT ds={DsId} rows={Rows}", ds.Id, hit.RowCount);
            return hit;
        }

        var result = await _inner.ExecuteReadOnlyAsync(ds, sql, maxRows);
        if (result.Success && result.RowCount <= MaxResultRowsToCache)
        {
            var ttl = IsSchemaQuery(sql) ? SchemaTtl : SqlTtl;
            Store(ds.Id, key, result, ttl);
            _logger.LogDebug("Query cache MISS ds={DsId} cached rows={Rows} ttl={Ttl}s", ds.Id, result.RowCount, ttl.TotalSeconds);
        }
        return result;
    }

    private static bool IsSchemaQuery(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return false;
        // SQL Server / PostgreSQL / MySQL schema introspection + Power BI DMV/INFO functions.
        return sql.Contains("INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("information_schema", StringComparison.Ordinal)
            || sql.Contains("INFO.TABLES", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("INFO.COLUMNS", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("$SYSTEM.", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<QueryExecutionResult> ExecuteRestApiAsync(Datasource ds, int maxRows = 1000)
    {
        if (ds == null) return await _inner.ExecuteRestApiAsync(ds!, maxRows);

        var key = $"rest:{ds.Id}:{maxRows}";
        if (_cache.TryGetValue<QueryExecutionResult>(key, out var hit) && hit != null)
            return hit;

        var result = await _inner.ExecuteRestApiAsync(ds, maxRows);
        if (result.Success && result.RowCount <= MaxResultRowsToCache)
            Store(ds.Id, key, result, RestTtl);
        return result;
    }

    // Pass-through — connection tests must always hit the live server.
    public Task<(bool Success, string? Error)> TestConnectionAsync(
        string type, string connectionString, string? dbUser = null, string? dbPassword = null,
        string? xmlaEndpoint = null, string? tenantId = null)
        => _inner.TestConnectionAsync(type, connectionString, dbUser, dbPassword, xmlaEndpoint, tenantId);

    public Task<(bool Success, string? Error)> TestRestApiAsync(string? apiUrl, string? apiKey, string? apiMethod = null)
        => _inner.TestRestApiAsync(apiUrl, apiKey, apiMethod);

    public void InvalidateDatasource(int datasourceId)
    {
        if (_tokens.TryRemove(datasourceId, out var cts))
        {
            try { cts.Cancel(); } catch { /* ignore */ }
            cts.Dispose();
            _logger.LogInformation("Query cache invalidated for datasource {DsId}.", datasourceId);
        }
    }

    // ── helpers ─────────────────────────────────────────────────
    private void Store(int datasourceId, string key, QueryExecutionResult value, TimeSpan ttl)
    {
        var cts = _tokens.GetOrAdd(datasourceId, _ => new CancellationTokenSource());
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Size = Math.Max(1, value.RowCount)
        };
        entryOptions.ExpirationTokens.Add(new CancellationChangeToken(cts.Token));
        _cache.Set(key, value, entryOptions);
    }

    private static string BuildSqlKey(int datasourceId, string sql, int maxRows)
    {
        var normalized = (sql ?? "").Trim();
        using var sha = SHA1.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized)));
        return $"sql:{datasourceId}:{maxRows}:{hash}";
    }
}
