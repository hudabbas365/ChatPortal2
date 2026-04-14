using AIInsights.Data;
using AIInsights.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.Controllers;

[Authorize]
public class BillingController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;

    public BillingController(AppDbContext db, UserManager<ApplicationUser> userManager, IConfiguration config)
    {
        _db = db;
        _userManager = userManager;
        _config = config;
    }

    private async Task<ApplicationUser?> GetCallerAsync()
    {
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        return string.IsNullOrEmpty(callerId) ? null : await _db.Users.FindAsync(callerId);
    }

    private bool IsOrgAdminOf(ApplicationUser caller, int organizationId)
    {
        if (caller.Role == "SuperAdmin") return true;
        return caller.Role == "OrgAdmin" && caller.OrganizationId == organizationId;
    }

    [HttpGet("/admin/billing")]
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var user = await _db.Users
            .Include(u => u.Subscription)
            .FirstOrDefaultAsync(u => u.Id == userId);

        ViewBag.User = user;
        ViewBag.Plan = user?.Subscription;
        ViewBag.StripePublishableKey = _config["Stripe:PublishableKey"] ?? "";
        return View("~/Views/Admin/Billing.cshtml");
    }

    [HttpPost("/api/billing/subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return Unauthorized();

        // Store only masked card info — never full card numbers
        if (!string.IsNullOrEmpty(req.CardLast4))
        {
            user.CardLast4 = req.CardLast4;
            user.CardBrand = req.CardBrand ?? "Unknown";
            await _userManager.UpdateAsync(user);
        }

        // Update subscription plan
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == userId);
        if (plan != null && !string.IsNullOrEmpty(req.PlanType))
        {
            if (Enum.TryParse<PlanType>(req.PlanType, true, out var planType))
            {
                plan.Plan = planType;
                await _db.SaveChangesAsync();
            }
        }

        return Ok(new { success = true, message = "Subscription updated successfully." });
    }

    [HttpPost("/api/billing/token-packs")]
    public async Task<IActionResult> BuyTokenPacks([FromBody] BuyTokenPacksRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null || !IsOrgAdminOf(caller, req.OrganizationId))
            return StatusCode(403, new { error = "Only Organization Admins can buy token packs." });

        var org = await _db.Organizations.FindAsync(req.OrganizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });
        if (org.Plan != PlanType.Enterprise)
            return BadRequest(new { error = "Token packs are available for Enterprise plan only." });

        var packs = req.Packs <= 0 ? 1 : req.Packs;
        org.EnterpriseExtraTokenPacks += packs;
        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "enterprise_token_pack_purchased",
            Description = $"{packs} enterprise token pack(s) purchased.",
            UserId = caller.Id,
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();
        return Ok(new
        {
            success = true,
            packs = org.EnterpriseExtraTokenPacks,
            totalExtraTokens = org.EnterpriseExtraTokenPacks * 2_000_000
        });
    }

    [HttpPost("/api/billing/assign-license")]
    public async Task<IActionResult> AssignLicense([FromBody] AssignLicenseRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null || !IsOrgAdminOf(caller, req.OrganizationId))
            return StatusCode(403, new { error = "Only Organization Admins can assign licenses." });

        var user = await _userManager.FindByIdAsync(req.UserId);
        if (user == null) return NotFound(new { error = "User not found." });

        var org = await _db.Organizations.FindAsync(req.OrganizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        if (!Enum.TryParse<PlanType>(req.Plan, true, out var planType))
            return BadRequest(new { error = "Invalid plan. Use: Free, FreeTrial, Professional, Enterprise" });

        var sub = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == user.Id);
        if (sub == null)
        {
            sub = new SubscriptionPlan { UserId = user.Id, Plan = planType };
            _db.SubscriptionPlans.Add(sub);
        }
        else
        {
            sub.Plan = planType;
        }

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "license_assigned",
            Description = $"License '{req.Plan}' assigned to user '{user.Email}' by admin.",
            UserId = caller.Id,
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}

public class SubscribeRequest
{
    public string? PaymentMethodId { get; set; }
    public string? PlanType { get; set; }
    public string? CardholderName { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardBrand { get; set; }
}

public class BuyTokenPacksRequest
{
    public int OrganizationId { get; set; }
    public int Packs { get; set; } = 1;
}

public class AssignLicenseRequest
{
    public int OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    public string Plan { get; set; } = "Free";
}
