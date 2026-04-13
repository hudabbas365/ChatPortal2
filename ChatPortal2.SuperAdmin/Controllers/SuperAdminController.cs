using ChatPortal2.Data;
using ChatPortal2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChatPortal2.SuperAdmin.Controllers;

[Authorize]
public class SuperAdminController : Controller
{
    private readonly AppDbContext _db;

    public SuperAdminController(AppDbContext db)
    {
        _db = db;
    }

    private string? GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    // Verifies SuperAdmin role both from JWT claims AND database for defense-in-depth
    private async Task<bool> IsSuperAdminAsync()
    {
        if (!User.Claims.Any(c => c.Type == "role" && c.Value == "SuperAdmin"))
            return false;
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId)) return false;
        var user = await _db.Users.FindAsync(userId) as ApplicationUser;
        return user?.Role == "SuperAdmin";
    }

    [HttpGet("/superadmin")]
    public async Task<IActionResult> Index()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        ViewBag.TotalOrgs = await _db.Organizations.CountAsync();
        ViewBag.TotalUsers = await _db.Users.CountAsync();
        ViewBag.TotalWorkspaces = await _db.Workspaces.CountAsync();
        ViewBag.TotalMessages = await _db.ChatMessages.CountAsync();

        // Income calculation: Professional ($25/user) + Enterprise ($35/user)
        var plans = await _db.SubscriptionPlans.ToListAsync();
        var proCount = plans.Count(p => p.Plan == PlanType.Professional);
        var enterpriseCount = plans.Count(p => p.Plan == PlanType.Enterprise);
        ViewBag.ProUsers = proCount;
        ViewBag.EnterpriseUsers = enterpriseCount;
        ViewBag.TotalIncome = proCount * PlanPricing.ProPricePerUser + enterpriseCount * PlanPricing.EnterprisePricePerUser;
        ViewBag.ActiveTrials = plans.Count(p => p.IsTrialActive);

        return View("~/Views/Admin/Index.cshtml");
    }

    [HttpGet("/superadmin/organizations")]
    public async Task<IActionResult> Organizations()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var orgs = await _db.Organizations
            .Include(o => o.Users)
                .ThenInclude(u => u.Subscription)
            .Include(o => o.Workspaces)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return View("~/Views/Admin/Organizations.cshtml", orgs);
    }

    [HttpGet("/superadmin/activity")]
    public async Task<IActionResult> ActivityLogs([FromQuery] int page = 1)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        const int pageSize = 50;
        var logs = await _db.ActivityLogs
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        ViewBag.Page = page;
        return View("~/Views/Admin/ActivityLogs.cshtml", logs);
    }

    [HttpGet("/superadmin/aiconfig")]
    public async Task<IActionResult> AiConfig()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        return View("~/Views/Admin/AiConfig.cshtml");
    }

    [HttpGet("/superadmin/revenue")]
    public async Task<IActionResult> Payments()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var plans = await _db.SubscriptionPlans
            .Include(p => p.User)
            .ToListAsync();

        var proCount = plans.Count(p => p.Plan == PlanType.Professional);
        var enterpriseCount = plans.Count(p => p.Plan == PlanType.Enterprise);
        ViewBag.ProCount = proCount;
        ViewBag.EnterpriseCount = enterpriseCount;
        ViewBag.ProRevenue = proCount * PlanPricing.ProPricePerUser;
        ViewBag.EnterpriseRevenue = enterpriseCount * PlanPricing.EnterprisePricePerUser;
        ViewBag.TotalIncome = proCount * PlanPricing.ProPricePerUser + enterpriseCount * PlanPricing.EnterprisePricePerUser;
        ViewBag.ActiveTrials = plans.Count(p => p.IsTrialActive);
        ViewBag.ExpiredTrials = plans.Count(p => p.IsTrialExpired);

        var paidUsers = await _db.Users
            .Where(u => u.CardLast4 != null)
            .Select(u => new { u.Id, u.FullName, u.Email, u.CardBrand, u.CardLast4 })
            .ToListAsync();
        ViewBag.PaidUsers = paidUsers;

        return View("~/Views/Admin/Revenue.cshtml");
    }

    [HttpGet("/superadmin/seo")]
    public async Task<IActionResult> Seo()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var entries = await _db.SeoEntries.OrderBy(s => s.PageUrl).ToListAsync();
        return View("~/Views/Admin/Seo.cshtml", entries);
    }

    [HttpPost("/superadmin/seo/save")]
    public async Task<IActionResult> SaveSeo([FromBody] SeoEntry entry)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        if (entry.Id > 0)
        {
            var existing = await _db.SeoEntries.FindAsync(entry.Id);
            if (existing == null) return NotFound();
            existing.Title = entry.Title;
            existing.MetaDescription = entry.MetaDescription;
            existing.MetaKeywords = entry.MetaKeywords;
            existing.OgTitle = entry.OgTitle;
            existing.OgDescription = entry.OgDescription;
            existing.RobotsDirective = entry.RobotsDirective;
            existing.LastModified = DateTime.UtcNow;
        }
        else
        {
            entry.LastModified = DateTime.UtcNow;
            entry.CreatedAt = DateTime.UtcNow;
            _db.SeoEntries.Add(entry);
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}
