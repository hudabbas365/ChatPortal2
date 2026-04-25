using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.SuperAdmin.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public class SecurityController : SuperAdminController
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public SecurityController(AppDbContext db, UserManager<ApplicationUser> userManager, CohereService cohere)
        : base(db, cohere)
    {
        _db = db;
        _userManager = userManager;
    }

    // ─── D22: Failed-login / lockout monitor ───────────────────────────────

    [HttpGet("/superadmin/security/logins")]
    public async Task<IActionResult> FailedLogins()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var now = DateTimeOffset.UtcNow;

        var users = await _db.Users
            .Include(u => u.Organization)
            .Where(u => u.AccessFailedCount > 0 || (u.LockoutEnd != null && u.LockoutEnd > now))
            .OrderByDescending(u => u.AccessFailedCount)
            .Select(u => new FailedLoginUserVm
            {
                Id = u.Id,
                Email = u.Email ?? "",
                FullName = u.FullName,
                OrgName = u.Organization != null ? u.Organization.Name : null,
                AccessFailedCount = u.AccessFailedCount,
                LockoutEnd = u.LockoutEnd,
                LastLoginAt = u.LastLoginAt,
                LastLoginIp = u.LastLoginIp,
                LastLoginCountry = u.LastLoginCountry
            })
            .ToListAsync();

        var failedEvents = await _db.ActivityLogs
            .Where(l => l.Action == "Auth.LoginFailed")
            .OrderByDescending(l => l.CreatedAt)
            .Take(200)
            .ToListAsync();

        ViewBag.FailedEvents = failedEvents;
        return View("~/Views/Admin/SecurityLogins.cshtml", users);
    }

    [HttpPost("/api/superadmin/security/users/{id}/reset-failed-attempts")]
    public async Task<IActionResult> ResetFailedAttempts(string id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { error = "User not found." });

        await _userManager.ResetAccessFailedCountAsync(user);

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Security.ResetFailedAttempts",
            Description = $"SuperAdmin reset failed attempts for {user.Email}",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpPost("/api/superadmin/security/users/{id}/unlock")]
    public async Task<IActionResult> UnlockUser(string id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { error = "User not found." });

        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Security.Unlock",
            Description = $"SuperAdmin unlocked account {user.Email}",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpPost("/api/superadmin/security/users/{id}/force-password-reset")]
    public async Task<IActionResult> ForcePasswordReset(string id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { error = "User not found." });

        user.MustChangePassword = true;
        await _userManager.UpdateAsync(user);

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Security.ForcePasswordReset",
            Description = $"SuperAdmin forced password reset for {user.Email}",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    // ─── D24: Audit export ─────────────────────────────────────────────────

    [HttpGet("/superadmin/activity/export.csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? action, [FromQuery] string? userId,
        [FromQuery] int? orgId, [FromQuery] string? search)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        return await ExportInternal(from, to, action, userId, orgId, search, "csv");
    }

    [HttpGet("/superadmin/activity/export.xlsx")]
    public async Task<IActionResult> ExportXlsx(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? action, [FromQuery] string? userId,
        [FromQuery] int? orgId, [FromQuery] string? search)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        return await ExportInternal(from, to, action, userId, orgId, search, "xlsx");
    }

    private async Task<IActionResult> ExportInternal(
        DateTime? from, DateTime? to, string? action, string? userId,
        int? orgId, string? search, string format)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = (to ?? DateTime.UtcNow).AddDays(1);

        // Build query
        var query = from log in _db.ActivityLogs
                    join user in _db.Users on log.UserId equals user.Id into uj
                    from user in uj.DefaultIfEmpty()
                    join org in _db.Organizations on log.OrganizationId equals org.Id into oj
                    from org in oj.DefaultIfEmpty()
                    where log.CreatedAt >= fromDate && log.CreatedAt < toDate
                    select new ExportRow
                    {
                        CreatedAt = log.CreatedAt,
                        Action = log.Action,
                        Description = log.Description,
                        UserId = log.UserId,
                        UserEmail = user != null ? user.Email : null,
                        UserName = user != null ? user.FullName : null,
                        OrganizationId = log.OrganizationId,
                        OrganizationName = org != null ? org.Name : null,
                        Ip = user != null ? user.LastLoginIp : null,
                        Country = user != null ? user.LastLoginCountry : null
                    };

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(l => l.Action == action);
        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(l => l.UserId == userId);
        if (orgId.HasValue)
            query = query.Where(l => l.OrganizationId == orgId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(l =>
                (l.UserName != null && l.UserName.ToLower().Contains(term)) ||
                (l.UserEmail != null && l.UserEmail.ToLower().Contains(term)) ||
                (l.OrganizationName != null && l.OrganizationName.ToLower().Contains(term)) ||
                l.Action.ToLower().Contains(term) ||
                l.Description.ToLower().Contains(term));
        }

        // Hard cap check
        const int MaxRows = 250_000;
        var count = await query.CountAsync();
        if (count > MaxRows)
            return BadRequest(new { error = $"Export would return {count:N0} rows which exceeds the 250,000-row limit. Please narrow your date range or filters." });

        if (format == "csv")
        {
            Response.Headers.Append("Content-Disposition",
                $"attachment; filename=\"audit-{fromDate:yyyyMMdd}-{(toDate.AddDays(-1)):yyyyMMdd}.csv\"");
            Response.ContentType = "text/csv";

            // Stream directly to response body to avoid large in-memory buffers
            await using var writer = new StreamWriter(Response.Body, leaveOpen: true);
            await writer.WriteLineAsync("CreatedAtUtc,Action,Description,UserId,UserEmail,UserName,OrganizationId,OrganizationName,Ip,Country");

            await foreach (var row in query.OrderBy(l => l.CreatedAt).AsAsyncEnumerable())
            {
                await writer.WriteLineAsync(string.Join(",",
                    CsvEscape(row.CreatedAt.ToString("O")),
                    CsvEscape(row.Action),
                    CsvEscape(row.Description),
                    CsvEscape(row.UserId),
                    CsvEscape(row.UserEmail),
                    CsvEscape(row.UserName),
                    CsvEscape(row.OrganizationId?.ToString()),
                    CsvEscape(row.OrganizationName),
                    CsvEscape(row.Ip),
                    CsvEscape(row.Country)));
            }

            await writer.FlushAsync();
            return new EmptyResult();
        }
        else // xlsx
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("ActivityLogs");

            // Header row
            var headers = new[] { "CreatedAtUtc", "Action", "Description", "UserId", "UserEmail", "UserName", "OrganizationId", "OrganizationName", "Ip", "Country" };
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];
            ws.Row(1).Style.Font.Bold = true;
            ws.SheetView.Freeze(1, 0);
            var range = ws.Range(1, 1, 1, headers.Length);
            range.SetAutoFilter();

            int row = 2;
            await foreach (var r in query.OrderBy(l => l.CreatedAt).AsAsyncEnumerable())
            {
                ws.Cell(row, 1).Value = r.CreatedAt.ToString("O");
                ws.Cell(row, 2).Value = XlsxSafe(r.Action);
                ws.Cell(row, 3).Value = XlsxSafe(r.Description);
                ws.Cell(row, 4).Value = r.UserId;
                ws.Cell(row, 5).Value = r.UserEmail ?? "";
                ws.Cell(row, 6).Value = r.UserName ?? "";
                ws.Cell(row, 7).Value = r.OrganizationId?.ToString() ?? "";
                ws.Cell(row, 8).Value = r.OrganizationName ?? "";
                ws.Cell(row, 9).Value = r.Ip ?? "";
                ws.Cell(row, 10).Value = r.Country ?? "";
                row++;
            }

            ws.Columns().AdjustToContents();

            // MemoryStream is intentionally not disposed here: FileStreamResult owns and disposes it after the response is sent.
            var ms = new MemoryStream();
            workbook.SaveAs(ms);
            ms.Position = 0;
            var fileName = $"audit-{fromDate:yyyyMMdd}-{(toDate.AddDays(-1)):yyyyMMdd}.xlsx";
            return File(ms,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
    }

    /// <summary>
    /// Escapes a value for CSV, and prefixes formula-injection characters with a tab to
    /// prevent them from being evaluated as formulas when opened in Excel.
    /// </summary>
    private static string CsvEscape(string? value)
    {
        if (value == null) return "";
        // Mitigate CSV formula injection: prefix cells starting with formula chars
        if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@'))
            value = "\t" + value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    /// <summary>
    /// Sanitises an XLSX cell value to prevent formula injection when opened in Excel.
    /// </summary>
    private static string XlsxSafe(string? value)
    {
        if (value == null) return "";
        if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@'))
            return "'" + value;
        return value;
    }

    // ─── D27: Integration health ───────────────────────────────────────────

    [HttpGet("/superadmin/security/integrations")]
    public async Task<IActionResult> Integrations()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var vm = await BuildIntegrationHealthVmAsync();
        return View("~/Views/Admin/IntegrationHealth.cshtml", vm);
    }

    [HttpGet("/api/superadmin/security/integrations")]
    public async Task<IActionResult> IntegrationsJson()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var vm = await BuildIntegrationHealthVmAsync();
        return Ok(vm);
    }

    private async Task<IntegrationHealthVm> BuildIntegrationHealthVmAsync()
    {
        var cutoff24h = DateTime.UtcNow.AddHours(-24);
        var providers = new[] { "Cohere", "Smtp", "PayPal" };

        var allChecks = await _db.IntegrationHealthChecks
            .Where(h => h.CreatedAt >= cutoff24h)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync();

        var items = providers.Select(p =>
        {
            var providerChecks = allChecks.Where(h => h.Provider == p).ToList();
            var latest = providerChecks.FirstOrDefault();
            var total = providerChecks.Count;
            var upCount = providerChecks.Count(h => h.Status == "Up");
            var uptime = total > 0 ? (double)upCount / total * 100 : 100;
            var last20 = providerChecks.Take(20).ToList();
            return new IntegrationProviderVm
            {
                Provider = p,
                Status = latest?.Status ?? "Unknown",
                LatencyMs = latest?.LatencyMs ?? 0,
                LastError = latest?.Error,
                LastChecked = latest?.CreatedAt,
                UptimePercent24h = Math.Round(uptime, 1),
                Last20Events = last20
            };
        }).ToList();

        return new IntegrationHealthVm
        {
            Providers = items,
            AnyDown = items.Any(i => i.Status == "Down")
        };
    }

    // ─── VMs and helpers ──────────────────────────────────────────────────

    public class FailedLoginUserVm
    {
        public string Id { get; set; } = "";
        public string Email { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? OrgName { get; set; }
        public int AccessFailedCount { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string? LastLoginIp { get; set; }
        public string? LastLoginCountry { get; set; }
    }

    public class ExportRow
    {
        public DateTime CreatedAt { get; set; }
        public string Action { get; set; } = "";
        public string Description { get; set; } = "";
        public string UserId { get; set; } = "";
        public string? UserEmail { get; set; }
        public string? UserName { get; set; }
        public int? OrganizationId { get; set; }
        public string? OrganizationName { get; set; }
        public string? Ip { get; set; }
        public string? Country { get; set; }
    }

    public class IntegrationHealthVm
    {
        public List<IntegrationProviderVm> Providers { get; set; } = new();
        public bool AnyDown { get; set; }
    }

    public class IntegrationProviderVm
    {
        public string Provider { get; set; } = "";
        public string Status { get; set; } = "";
        public int LatencyMs { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastChecked { get; set; }
        public double UptimePercent24h { get; set; }
        public List<IntegrationHealthCheck> Last20Events { get; set; } = new();
    }
}
