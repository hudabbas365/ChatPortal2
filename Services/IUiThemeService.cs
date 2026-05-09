using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace AIInsights.Services;

/// <summary>
/// Read facade over <see cref="UiThemeVariable"/>: returns the current theme
/// as the body of <c>:root { … }</c> for <c>GET /css/theme.css</c>, and
/// invalidates an in-memory cache whenever an admin saves a change so the
/// next request picks up the new values without restarting the app.
/// </summary>
public interface IUiThemeService
{
    Task<string> GetThemeCssAsync(CancellationToken ct = default);
    Task<List<UiThemeVariable>> GetAllAsync(CancellationToken ct = default);
    void InvalidateCache();
}

public class UiThemeService : IUiThemeService
{
    private const string CacheKey = "ui-theme:css";
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public UiThemeService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public Task<List<UiThemeVariable>> GetAllAsync(CancellationToken ct = default) =>
        _db.UiThemeVariables
            .AsNoTracking()
            .OrderBy(v => v.Category)
            .ThenBy(v => v.SortOrder)
            .ThenBy(v => v.Key)
            .ToListAsync(ct);

    public async Task<string> GetThemeCssAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue<string>(CacheKey, out var cached) && !string.IsNullOrEmpty(cached))
            return cached!;

        var rows = await _db.UiThemeVariables.AsNoTracking().ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("/* Generated from DB-backed UI theme registry. Edit at /superadmin/ui-theme. */");
        sb.AppendLine(":root {");
        foreach (var v in rows.OrderBy(r => r.Key))
        {
            // Sanitize the value to a single CSS declaration line — strip any
            // line breaks or `}` chars an admin might paste accidentally so we
            // never break the global stylesheet.
            var safe = (v.Value ?? "")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("}", "")
                .Trim();
            sb.Append("    --").Append(v.Key).Append(": ").Append(safe).AppendLine(";");
        }
        sb.AppendLine("}");
        var css = sb.ToString();
        _cache.Set(CacheKey, css, TimeSpan.FromMinutes(15));
        return css;
    }

    public void InvalidateCache() => _cache.Remove(CacheKey);
}
