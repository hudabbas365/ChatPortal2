using AIInsights.Data;
using AIInsights.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.SuperAdmin.Controllers;

[Authorize]
public class LicensesController : Controller
{
    private readonly AppDbContext _db;

    public LicensesController(AppDbContext db)
    {
        _db = db;
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

    private string GetLicenseStatus(Organization org)
    {
        if (org.Plan == PlanType.Free || org.Plan == PlanType.FreeTrial)
            return "None";
        if (org.LicenseEndsAt == null)
            return "Active";
        var now = DateTime.UtcNow;
        if (org.LicenseEndsAt <= now)
            return "Expired";
        if (org.LicenseEndsAt <= now.AddDays(14))
            return "ExpiringSoon";
        return "Active";
    }

    // ──── GET /superadmin/licenses ────
    [HttpGet("/superadmin/licenses")]
    public async Task<IActionResult> Index(
        [FromQuery] string? search = null,
        [FromQuery] string? plan = null,
        [FromQuery] string? status = null)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var orgs = await _db.Organizations
            .OrderBy(o => o.Name)
            .ToListAsync();

        // Build assigned counts in a single query
        var assignedByOrg = await _db.SubscriptionPlans
            .Where(s => s.User!.OrganizationId != null
                        && (s.Plan == PlanType.Professional || s.Plan == PlanType.Enterprise))
            .GroupBy(s => s.User!.OrganizationId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToListAsync();

        var assignedDict = assignedByOrg.ToDictionary(x => x.OrgId!.Value, x => x.Count);

        var now = DateTime.UtcNow;
        var rows = orgs.Select(org =>
        {
            var assigned = assignedDict.TryGetValue(org.Id, out var c) ? c : 0;
            var free = Math.Max(0, org.PurchasedLicenses - assigned);
            var licStatus = GetLicenseStatus(org);
            return new LicenseRowViewModel
            {
                Id = org.Id,
                Name = org.Name,
                Plan = org.Plan.ToString(),
                PurchasedLicenses = org.PurchasedLicenses,
                AssignedLicenses = assigned,
                FreeLicenses = free,
                LicenseStartsAt = org.LicenseStartsAt,
                LicenseEndsAt = org.LicenseEndsAt,
                AutoRenew = org.AutoRenew,
                LicenseNotes = org.LicenseNotes,
                Status = licStatus
            };
        }).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            rows = rows.Where(r => r.Name.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(plan))
            rows = rows.Where(r => r.Plan.Equals(plan, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(status))
            rows = rows.Where(r => r.Status.Equals(status, StringComparison.OrdinalIgnoreCase));

        ViewBag.Search = search;
        ViewBag.PlanFilter = plan;
        ViewBag.StatusFilter = status;
        ViewData["Title"] = "License Management";
        ViewData["ActivePage"] = "licenses";

        return View("~/Views/Admin/Licenses.cshtml", rows.ToList());
    }

    // ──── POST /api/superadmin/orgs/{id}/licenses/grant ────
    [HttpPost("/api/superadmin/orgs/{id}/licenses/grant")]
    public async Task<IActionResult> Grant(int id, [FromBody] GrantLicenseRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (req == null || req.Count <= 0)
            return BadRequest(new { error = "count must be greater than 0." });

        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        var callerId = GetCurrentUserId();
        var callerEmail = User.FindFirstValue(ClaimTypes.Email)
                          ?? User.FindFirstValue("email");

        var fromLicenses = org.PurchasedLicenses;
        var fromEndsAt = org.LicenseEndsAt;

        org.PurchasedLicenses += req.Count;
        if (req.ExpiresAt.HasValue)
        {
            org.LicenseEndsAt = req.ExpiresAt;
            if (org.LicenseStartsAt == null)
                org.LicenseStartsAt = DateTime.UtcNow;
        }
        if (req.AutoRenew.HasValue)
            org.AutoRenew = req.AutoRenew.Value;

        _db.PlanChangeLogs.Add(new PlanChangeLog
        {
            OrganizationId = id,
            FromPlan = org.Plan.ToString(),
            ToPlan = org.Plan.ToString(),
            FromPurchasedLicenses = fromLicenses,
            ToPurchasedLicenses = org.PurchasedLicenses,
            FromLicenseEndsAt = fromEndsAt,
            ToLicenseEndsAt = org.LicenseEndsAt,
            ChangeType = "LicenseGrant",
            Reason = req.Reason,
            ChangedByUserId = callerId,
            ChangedByEmail = callerEmail,
            CreatedAt = DateTime.UtcNow
        });

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "License.Grant",
            Description = $"Granted {req.Count} license(s) to {org.Name}. Reason: {req.Reason ?? "—"}",
            UserId = callerId ?? "",
            OrganizationId = id,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var assigned = await _db.SubscriptionPlans
            .CountAsync(s => s.User!.OrganizationId == id
                             && (s.Plan == PlanType.Professional || s.Plan == PlanType.Enterprise));

        return Ok(new
        {
            success = true,
            orgId = id,
            purchasedLicenses = org.PurchasedLicenses,
            assignedLicenses = assigned,
            freeLicenses = Math.Max(0, org.PurchasedLicenses - assigned),
            licenseStartsAt = org.LicenseStartsAt,
            licenseEndsAt = org.LicenseEndsAt,
            autoRenew = org.AutoRenew
        });
    }

    // ──── POST /api/superadmin/orgs/{id}/licenses/revoke ────
    [HttpPost("/api/superadmin/orgs/{id}/licenses/revoke")]
    public async Task<IActionResult> Revoke(int id, [FromBody] RevokeLicenseRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (req == null || req.Count <= 0)
            return BadRequest(new { error = "count must be greater than 0." });

        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        var assignedCount = await _db.SubscriptionPlans
            .CountAsync(s => s.User!.OrganizationId == id
                             && (s.Plan == PlanType.Professional || s.Plan == PlanType.Enterprise));

        var newCount = org.PurchasedLicenses - req.Count;
        if (newCount < assignedCount)
            return BadRequest(new { error = $"Cannot reduce licenses below the {assignedCount} already assigned. Revoke user licenses first." });
        if (newCount < 0)
            return BadRequest(new { error = "Cannot reduce licenses below 0." });

        var callerId = GetCurrentUserId();
        var callerEmail = User.FindFirstValue(ClaimTypes.Email)
                          ?? User.FindFirstValue("email");

        var fromLicenses = org.PurchasedLicenses;
        org.PurchasedLicenses = newCount;

        _db.PlanChangeLogs.Add(new PlanChangeLog
        {
            OrganizationId = id,
            FromPlan = org.Plan.ToString(),
            ToPlan = org.Plan.ToString(),
            FromPurchasedLicenses = fromLicenses,
            ToPurchasedLicenses = org.PurchasedLicenses,
            FromLicenseEndsAt = org.LicenseEndsAt,
            ToLicenseEndsAt = org.LicenseEndsAt,
            ChangeType = "LicenseRevoke",
            Reason = req.Reason,
            ChangedByUserId = callerId,
            ChangedByEmail = callerEmail,
            CreatedAt = DateTime.UtcNow
        });

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "License.Revoke",
            Description = $"Revoked {req.Count} license(s) from {org.Name}. Reason: {req.Reason ?? "—"}",
            UserId = callerId ?? "",
            OrganizationId = id,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            orgId = id,
            purchasedLicenses = org.PurchasedLicenses,
            assignedLicenses = assignedCount,
            freeLicenses = Math.Max(0, org.PurchasedLicenses - assignedCount)
        });
    }

    // ──── PUT /api/superadmin/orgs/{id}/licenses/term ────
    [HttpPut("/api/superadmin/orgs/{id}/licenses/term")]
    public async Task<IActionResult> UpdateTerm(int id, [FromBody] UpdateTermRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (req == null) return BadRequest(new { error = "Request body required." });

        if (req.StartsAt.HasValue && req.EndsAt.HasValue && req.EndsAt <= req.StartsAt)
            return BadRequest(new { error = "endsAt must be after startsAt." });

        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        var callerId = GetCurrentUserId();
        var callerEmail = User.FindFirstValue(ClaimTypes.Email)
                          ?? User.FindFirstValue("email");

        var fromEndsAt = org.LicenseEndsAt;

        org.LicenseStartsAt = req.StartsAt;
        org.LicenseEndsAt = req.EndsAt;
        org.AutoRenew = req.AutoRenew;
        if (req.Notes != null) org.LicenseNotes = req.Notes;

        _db.PlanChangeLogs.Add(new PlanChangeLog
        {
            OrganizationId = id,
            FromPlan = org.Plan.ToString(),
            ToPlan = org.Plan.ToString(),
            FromPurchasedLicenses = org.PurchasedLicenses,
            ToPurchasedLicenses = org.PurchasedLicenses,
            FromLicenseEndsAt = fromEndsAt,
            ToLicenseEndsAt = org.LicenseEndsAt,
            ChangeType = "TermUpdate",
            ChangedByUserId = callerId,
            ChangedByEmail = callerEmail,
            CreatedAt = DateTime.UtcNow
        });

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "License.TermUpdate",
            Description = $"Updated license term for {org.Name}. Ends: {req.EndsAt?.ToString("yyyy-MM-dd") ?? "none"}, AutoRenew: {req.AutoRenew}",
            UserId = callerId ?? "",
            OrganizationId = id,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            orgId = id,
            licenseStartsAt = org.LicenseStartsAt,
            licenseEndsAt = org.LicenseEndsAt,
            autoRenew = org.AutoRenew
        });
    }

    // ──── GET /api/superadmin/orgs/{id}/licenses/history ────
    [HttpGet("/api/superadmin/orgs/{id}/licenses/history")]
    public async Task<IActionResult> History(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        var logs = await _db.PlanChangeLogs
            .Where(p => p.OrganizationId == id)
            .OrderByDescending(p => p.CreatedAt)
            .Take(100)
            .Select(p => new
            {
                p.Id,
                p.ChangeType,
                p.FromPlan,
                p.ToPlan,
                p.FromPurchasedLicenses,
                p.ToPurchasedLicenses,
                p.FromLicenseEndsAt,
                p.ToLicenseEndsAt,
                p.Reason,
                p.ChangedByEmail,
                p.ChangedByUserId,
                p.CreatedAt
            })
            .ToListAsync();

        return Ok(new { orgName = org.Name, logs });
    }

    // ──── Request DTOs ────
    public class GrantLicenseRequest
    {
        public int Count { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool? AutoRenew { get; set; }
        public string? Reason { get; set; }
    }

    public class RevokeLicenseRequest
    {
        public int Count { get; set; }
        public string? Reason { get; set; }
    }

    public class UpdateTermRequest
    {
        public DateTime? StartsAt { get; set; }
        public DateTime? EndsAt { get; set; }
        public bool AutoRenew { get; set; }
        public string? Notes { get; set; }
    }

    public class LicenseRowViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Plan { get; set; } = "";
        public int PurchasedLicenses { get; set; }
        public int AssignedLicenses { get; set; }
        public int FreeLicenses { get; set; }
        public DateTime? LicenseStartsAt { get; set; }
        public DateTime? LicenseEndsAt { get; set; }
        public bool AutoRenew { get; set; }
        public string? LicenseNotes { get; set; }
        public string Status { get; set; } = "";
    }
}
