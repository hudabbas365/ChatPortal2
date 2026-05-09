using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.SuperAdmin.Controllers;

/// <summary>
/// SuperAdmin CRUD over the DB-backed UI theme registry. Editing a row here
/// invalidates the in-memory cache, so the next request to <c>/css/theme.css</c>
/// re-emits with the new values and the entire UI rebrands without restart.
/// </summary>
[Authorize]
public class UiThemeController : Controller
{
    public static readonly string[] Kinds = new[] { "color", "gradient", "shadow", "radius", "size", "other" };

    private readonly AppDbContext _db;
    private readonly IUiThemeService _theme;

    public UiThemeController(AppDbContext db, IUiThemeService theme)
    {
        _db = db;
        _theme = theme;
    }

    private string? GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private async Task<bool> IsSuperAdminAsync()
    {
        if (!User.Claims.Any(c => c.Type == "role" && c.Value == "SuperAdmin"))
            return false;
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId)) return false;
        var user = await _db.Users.FindAsync(userId) as ApplicationUser;
        return user?.Role == "SuperAdmin";
    }

    // ──── GET /superadmin/ui-theme ────
    [HttpGet("/superadmin/ui-theme")]
    public async Task<IActionResult> Index()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var rows = await _db.UiThemeVariables
            .AsNoTracking()
            .OrderBy(v => v.Category)
            .ThenBy(v => v.SortOrder)
            .ThenBy(v => v.Key)
            .ToListAsync();

        ViewData["Title"] = "UI Theme";
        ViewData["ActivePage"] = "ui-theme";
        ViewBag.Kinds = Kinds;
        return View("~/Views/Admin/UiTheme.cshtml", rows);
    }

    public class ThemeUpdate
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    // ──── POST /superadmin/ui-theme/save ────
    [HttpPost("/superadmin/ui-theme/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] List<ThemeUpdate> updates)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (updates == null || updates.Count == 0)
            return Ok(new { saved = 0 });

        var keys = updates.Select(u => u.Key).Distinct().ToList();
        var rows = await _db.UiThemeVariables.Where(v => keys.Contains(v.Key)).ToListAsync();
        var byKey = rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);

        var userId = GetCurrentUserId();
        var nowUtc = DateTime.UtcNow;
        var saved = 0;
        foreach (var u in updates)
        {
            if (string.IsNullOrWhiteSpace(u.Key)) continue;
            if (!byKey.TryGetValue(u.Key, out var row)) continue;
            var newValue = (u.Value ?? "").Trim();
            if (newValue.Length == 0 || newValue.Length > 512) continue;
            if (string.Equals(row.Value, newValue, StringComparison.Ordinal)) continue;
            row.Value = newValue;
            row.UpdatedUtc = nowUtc;
            row.UpdatedBy = userId;
            saved++;
        }

        if (saved > 0)
        {
            await _db.SaveChangesAsync();
            _theme.InvalidateCache();
        }
        return Ok(new { saved });
    }

    // ──── POST /superadmin/ui-theme/{id}/reset ────
    [HttpPost("/superadmin/ui-theme/{id:int}/reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var row = await _db.UiThemeVariables.FirstOrDefaultAsync(v => v.Id == id);
        if (row == null) return NotFound();
        if (string.IsNullOrEmpty(row.DefaultValue)) return BadRequest(new { error = "No default recorded for this variable." });

        row.Value = row.DefaultValue!;
        row.UpdatedUtc = DateTime.UtcNow;
        row.UpdatedBy = GetCurrentUserId();
        await _db.SaveChangesAsync();
        _theme.InvalidateCache();
        return Ok(new { value = row.Value });
    }

    // ──── POST /superadmin/ui-theme/reset-all ────
    [HttpPost("/superadmin/ui-theme/reset-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAll()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var rows = await _db.UiThemeVariables
            .Where(v => v.DefaultValue != null && v.DefaultValue != "")
            .ToListAsync();
        var userId = GetCurrentUserId();
        var nowUtc = DateTime.UtcNow;
        foreach (var row in rows)
        {
            if (row.Value == row.DefaultValue) continue;
            row.Value = row.DefaultValue!;
            row.UpdatedUtc = nowUtc;
            row.UpdatedBy = userId;
        }
        await _db.SaveChangesAsync();
        _theme.InvalidateCache();
        return Ok(new { reset = rows.Count });
    }
}
