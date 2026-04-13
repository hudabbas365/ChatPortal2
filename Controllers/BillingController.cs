using ChatPortal2.Data;
using ChatPortal2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChatPortal2.Controllers;

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
}

public class SubscribeRequest
{
    public string? PaymentMethodId { get; set; }
    public string? PlanType { get; set; }
    public string? CardholderName { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardBrand { get; set; }
}
