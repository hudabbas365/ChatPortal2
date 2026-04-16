using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
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
    private readonly IPayPalService _payPal;

    public BillingController(AppDbContext db, UserManager<ApplicationUser> userManager, IConfiguration config, IPayPalService payPal)
    {
        _db = db;
        _userManager = userManager;
        _config = config;
        _payPal = payPal;
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

        // Check available licenses (Free assignments don't consume a license)
        if (planType != PlanType.Free)
        {
            var assignedCount = await _db.SubscriptionPlans
                .CountAsync(s => s.User != null && s.User.OrganizationId == req.OrganizationId && s.Plan != PlanType.Free);
            // Check if this user already has a paid license (re-assignment doesn't consume extra)
            var existingSub = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == req.UserId);
            var isReassignment = existingSub != null && existingSub.Plan != PlanType.Free;
            if (!isReassignment && assignedCount >= org.PurchasedLicenses)
                return BadRequest(new { error = $"No licenses available. You have {org.PurchasedLicenses} license(s) and {assignedCount} already assigned. Buy more licenses first." });
        }

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

    // ── License Info ──────────────────────────────────────────────
    [HttpGet("/api/billing/license-info")]
    public async Task<IActionResult> GetLicenseInfo([FromQuery] int organizationId)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();
        if (caller.Role != "SuperAdmin" && caller.OrganizationId != organizationId)
            return StatusCode(403, new { error = "Access denied." });

        var org = await _db.Organizations.FindAsync(organizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        var assignedCount = await _db.SubscriptionPlans
            .CountAsync(s => s.User != null && s.User.OrganizationId == organizationId && s.Plan != PlanType.Free);

        return Ok(new
        {
            purchased = org.PurchasedLicenses,
            assigned = assignedCount,
            available = Math.Max(0, org.PurchasedLicenses - assignedCount),
            plan = org.Plan.ToString()
        });
    }

    // ── Buy Licenses ──────────────────────────────────────────────
    [HttpPost("/api/billing/buy-licenses")]
    public async Task<IActionResult> BuyLicenses([FromBody] BuyLicensesRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null || !IsOrgAdminOf(caller, req.OrganizationId))
            return StatusCode(403, new { error = "Only Organization Admins can buy licenses." });

        var org = await _db.Organizations.FindAsync(req.OrganizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        var count = Math.Max(1, req.Count);
        org.PurchasedLicenses += count;

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "licenses_purchased",
            Description = $"{count} license(s) purchased. Total: {org.PurchasedLicenses}.",
            UserId = caller.Id,
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();

        var assignedCount = await _db.SubscriptionPlans
            .CountAsync(s => s.User != null && s.User.OrganizationId == req.OrganizationId && s.Plan != PlanType.Free);

        return Ok(new
        {
            success = true,
            purchased = org.PurchasedLicenses,
            assigned = assignedCount,
            available = Math.Max(0, org.PurchasedLicenses - assignedCount)
        });
    }

    // ── PayPal: Create Order ──────────────────────────────────────
    [HttpPost("/api/paypal/create-order")]
    public async Task<IActionResult> PayPalCreateOrder([FromBody] PayPalCreateOrderRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var returnUrl = $"{baseUrl}/OrgAdmin/Settings?tab=billing&paypal=success";
        var cancelUrl = $"{baseUrl}/OrgAdmin/Settings?tab=billing&paypal=cancel";

        var result = await _payPal.CreateOrderAsync(req.Amount, "USD", req.Description ?? "AIInsights Purchase", returnUrl, cancelUrl);
        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(new { orderId = result.OrderId, approveUrl = result.ApproveUrl });
    }

    // ── PayPal: Capture Order ─────────────────────────────────────
    [HttpPost("/api/paypal/capture-order")]
    public async Task<IActionResult> PayPalCaptureOrder([FromBody] PayPalCaptureOrderRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var capture = await _payPal.CaptureOrderAsync(req.OrderId);
        if (!capture.Success)
            return BadRequest(new { error = capture.Error });

        // Apply the purchase based on type
        var org = await _db.Organizations.FindAsync(req.OrganizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        var description = "";

        switch (req.PurchaseType)
        {
            case "license":
                var count = Math.Max(1, req.Quantity);
                org.PurchasedLicenses += count;
                description = $"{count} license(s) purchased via PayPal (Order: {req.OrderId}).";
                break;

            case "token_pack":
                var tokens = req.TokenAmount > 0 ? req.TokenAmount : 1_000_000;
                org.EnterpriseExtraTokenPacks += (int)Math.Ceiling(tokens / 2_000_000.0);
                description = $"{tokens:N0} extra tokens purchased via PayPal (Order: {req.OrderId}).";
                break;

            case "plan_upgrade":
                if (Enum.TryParse<PlanType>(req.PlanKey, true, out var planType))
                {
                    org.Plan = planType;
                    description = $"Plan upgraded to {req.PlanKey} via PayPal (Order: {req.OrderId}).";
                }
                break;

            default:
                return BadRequest(new { error = "Unknown purchase type." });
        }

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "paypal_payment_completed",
            Description = description,
            UserId = caller.Id,
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = description });
    }

    // ── Token Packages Info ───────────────────────────────────────
    [HttpGet("/api/billing/token-packages")]
    public IActionResult GetTokenPackages()
    {
        var packages = PlanPricing.TokenPackages.Select(p => new
        {
            name = p.Name,
            tokens = p.Tokens,
            price = p.Price
        });
        return Ok(packages);
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

public class BuyLicensesRequest
{
    public int OrganizationId { get; set; }
    public int Count { get; set; } = 1;
}

public class PayPalCreateOrderRequest
{
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}

public class PayPalCaptureOrderRequest
{
    public string OrderId { get; set; } = "";
    public int OrganizationId { get; set; }
    public string PurchaseType { get; set; } = ""; // "license", "token_pack", "plan_upgrade"
    public int Quantity { get; set; } = 1;
    public int TokenAmount { get; set; } = 0;
    public string? PlanKey { get; set; }
}
