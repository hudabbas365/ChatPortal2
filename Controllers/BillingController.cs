using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

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

        try
        {
            var result = await _payPal.CreateOrderAsync(req.Amount, "USD", req.Description ?? "AIInsights Purchase", returnUrl, cancelUrl);
            if (!result.Success)
                return BadRequest(new { error = result.Error });

            return Ok(new { orderId = result.OrderId, approveUrl = result.ApproveUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Payment service error: {ex.Message}" });
        }
    }

    // ── PayPal: Capture Order ─────────────────────────────────────
    [HttpPost("/api/paypal/capture-order")]
    public async Task<IActionResult> PayPalCaptureOrder([FromBody] PayPalCaptureOrderRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureOrderAsync(req.OrderId);
        }
        catch (Exception ex)
        {
            // Log failed payment
            _db.PaymentRecords.Add(new PaymentRecord
            {
                OrganizationId = req.OrganizationId,
                UserId = caller.Id,
                PaymentType = req.PurchaseType,
                Amount = 0,
                Status = "failed",
                PayPalOrderId = req.OrderId,
                Description = $"Capture exception: {req.PurchaseType}",
                ErrorMessage = ex.Message,
                PlanKey = req.PlanKey
            });
            await _db.SaveChangesAsync();
            return StatusCode(500, new { error = $"Payment capture error: {ex.Message}" });
        }
        if (!capture.Success)
        {
            // Log failed payment
            _db.PaymentRecords.Add(new PaymentRecord
            {
                OrganizationId = req.OrganizationId,
                UserId = caller.Id,
                PaymentType = req.PurchaseType,
                Amount = 0,
                Status = "failed",
                PayPalOrderId = req.OrderId,
                Description = $"Capture failed: {req.PurchaseType}",
                ErrorMessage = capture.Error,
                PlanKey = req.PlanKey
            });
            await _db.SaveChangesAsync();
            return BadRequest(new { error = capture.Error });
        }

        // Apply the purchase based on type
        var org = await _db.Organizations.FindAsync(req.OrganizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        var description = "";
        decimal amount = 0;

        switch (req.PurchaseType)
        {
            case "license":
                var count = Math.Max(1, req.Quantity);
                org.PurchasedLicenses += count;
                amount = count * (req.PlanKey?.ToLower() == "enterprise" ? 45m : 25m);
                description = $"{count} license(s) purchased via PayPal (Order: {req.OrderId}).";
                break;

            case "token_pack":
                var tokens = req.TokenAmount > 0 ? req.TokenAmount : 1_000_000;
                org.EnterpriseExtraTokenPacks += (int)Math.Ceiling(tokens / 2_000_000.0);
                amount = tokens <= 100000 ? 9m : tokens <= 500000 ? 20m : 25m;
                description = $"{tokens:N0} extra tokens purchased via PayPal (Order: {req.OrderId}).";
                break;

            case "plan_upgrade":
                if (Enum.TryParse<PlanType>(req.PlanKey, true, out var planType))
                {
                    org.Plan = planType;
                    amount = req.PlanKey?.ToLower() == "enterprise" ? 45m : 25m;
                    description = $"Plan upgraded to {req.PlanKey} via PayPal (Order: {req.OrderId}).";
                }
                break;

            default:
                return BadRequest(new { error = "Unknown purchase type." });
        }

        // Log successful payment
        _db.PaymentRecords.Add(new PaymentRecord
        {
            OrganizationId = req.OrganizationId,
            UserId = caller.Id,
            PaymentType = req.PurchaseType,
            Amount = amount,
            Status = "succeeded",
            PayPalOrderId = req.OrderId,
            Description = description,
            PlanKey = req.PlanKey
        });
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

    // ── PayPal Recurring Subscriptions ────────────────────────────

    [HttpPost("/api/paypal/create-subscription")]
    public async Task<IActionResult> PayPalCreateSubscription([FromBody] PayPalCreateSubscriptionRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();
        if (!IsOrgAdminOf(caller, req.OrganizationId))
            return StatusCode(403, new { error = "Only Organization Admins can manage subscriptions." });

        var org = await _db.Organizations.FindAsync(req.OrganizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        // Determine price from plan
        decimal monthlyPrice = req.PlanKey?.ToLower() switch
        {
            "enterprise" => 45.00m,
            "professional" => 25.00m,
            _ => 0
        };
        if (monthlyPrice == 0)
            return BadRequest(new { error = "Invalid plan. Choose Professional or Enterprise." });

        var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var returnUrl = $"{baseUrl}/OrgAdmin/Settings?tab=billing&subscription=success&orgId={req.OrganizationId}&planKey={req.PlanKey}";
        var cancelUrl = $"{baseUrl}/OrgAdmin/Settings?tab=billing&subscription=cancel";

        try
        {
            // 1. Create a PayPal product (or reuse — for simplicity create each time; PayPal deduplicates by name)
            var product = await _payPal.CreateProductAsync("AIInsights Subscription", "AIInsights monthly plan subscription");
            if (!product.Success)
                return BadRequest(new { error = product.Error });

            // 2. Create a billing plan
            var plan = await _payPal.CreatePlanAsync(product.ProductId, $"AIInsights {req.PlanKey} Monthly", monthlyPrice);
            if (!plan.Success)
                return BadRequest(new { error = plan.Error });

            // 3. Create the subscription
            var sub = await _payPal.CreateSubscriptionAsync(plan.PlanId, returnUrl, cancelUrl);
            if (!sub.Success)
                return BadRequest(new { error = sub.Error });

            // Store pending subscription info
            org.PayPalSubscriptionId = sub.SubscriptionId;
            org.PayPalPlanId = plan.PlanId;
            org.SubscriptionStatus = "APPROVAL_PENDING";
            await _db.SaveChangesAsync();

            return Ok(new { subscriptionId = sub.SubscriptionId, approveUrl = sub.ApproveUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Subscription error: {ex.Message}" });
        }
    }

    [HttpPost("/api/paypal/activate-subscription")]
    public async Task<IActionResult> PayPalActivateSubscription([FromBody] PayPalActivateSubscriptionRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var org = await _db.Organizations.FindAsync(req.OrganizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        if (string.IsNullOrEmpty(org.PayPalSubscriptionId))
            return BadRequest(new { error = "No pending subscription found." });

        try
        {
            // Verify subscription status with PayPal
            var details = await _payPal.GetSubscriptionDetailsAsync(org.PayPalSubscriptionId);
            if (details == null)
                return BadRequest(new { error = "Could not verify subscription with PayPal." });

            if (details.Status == "ACTIVE" || details.Status == "APPROVED")
            {
                // Activate the plan on the org
                if (Enum.TryParse<PlanType>(req.PlanKey, true, out var planType))
                {
                    org.Plan = planType;
                }
                org.SubscriptionStatus = "ACTIVE";
                org.SubscriptionStartDate = DateTime.UtcNow;
                org.SubscriptionNextBillingDate = details.NextBillingTime ?? DateTime.UtcNow.AddMonths(1);

                _db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "subscription_activated",
                    Description = $"Monthly {req.PlanKey} subscription activated (PayPal: {org.PayPalSubscriptionId}). Next billing: {org.SubscriptionNextBillingDate:yyyy-MM-dd}.",
                    UserId = caller.Id,
                    OrganizationId = req.OrganizationId
                });
                // Log successful subscription payment
                decimal subPrice = req.PlanKey?.ToLower() == "enterprise" ? 45m : 25m;
                _db.PaymentRecords.Add(new PaymentRecord
                {
                    OrganizationId = req.OrganizationId,
                    UserId = caller.Id,
                    PaymentType = "subscription",
                    Amount = subPrice,
                    Status = "succeeded",
                    PayPalSubscriptionId = org.PayPalSubscriptionId,
                    Description = $"Monthly {req.PlanKey} subscription activated",
                    PlanKey = req.PlanKey
                });
                await _db.SaveChangesAsync();

                return Ok(new { success = true, status = "ACTIVE", nextBilling = org.SubscriptionNextBillingDate });
            }

            return Ok(new { success = false, status = details.Status, error = $"Subscription status is {details.Status}, not yet active." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Activation error: {ex.Message}" });
        }
    }

    [HttpPost("/api/paypal/cancel-subscription")]
    public async Task<IActionResult> PayPalCancelSubscription([FromBody] PayPalCancelSubscriptionRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();
        if (!IsOrgAdminOf(caller, req.OrganizationId))
            return StatusCode(403, new { error = "Only Organization Admins can cancel subscriptions." });

        var org = await _db.Organizations.FindAsync(req.OrganizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        if (string.IsNullOrEmpty(org.PayPalSubscriptionId))
            return BadRequest(new { error = "No active subscription to cancel." });

        try
        {
            var cancelled = await _payPal.CancelSubscriptionAsync(org.PayPalSubscriptionId, req.Reason ?? "Customer requested cancellation");
            if (!cancelled)
                return BadRequest(new { error = "Failed to cancel subscription with PayPal." });

            org.SubscriptionStatus = "CANCELLED";
            // Keep plan active until end of current billing period
            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "subscription_cancelled",
                Description = $"Monthly subscription cancelled (PayPal: {org.PayPalSubscriptionId}). Plan remains active until {org.SubscriptionNextBillingDate:yyyy-MM-dd}.",
                UserId = caller.Id,
                OrganizationId = req.OrganizationId
            });
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = $"Subscription cancelled. Your plan remains active until {org.SubscriptionNextBillingDate:yyyy-MM-dd}." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Cancel error: {ex.Message}" });
        }
    }

    [HttpGet("/api/paypal/subscription-status")]
    public async Task<IActionResult> GetSubscriptionStatus([FromQuery] int organizationId)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var org = await _db.Organizations.FindAsync(organizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        return Ok(new
        {
            subscriptionId = org.PayPalSubscriptionId,
            status = org.SubscriptionStatus,
            plan = org.Plan.ToString(),
            startDate = org.SubscriptionStartDate,
            nextBillingDate = org.SubscriptionNextBillingDate
        });
    }

    // ── PayPal Webhook (recurring payment notifications) ─────────
    [HttpPost("/api/paypal/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PayPalWebhook()
    {
        string body;
        using (var reader = new StreamReader(Request.Body))
            body = await reader.ReadToEndAsync();

        try
        {
            var doc = JsonDocument.Parse(body);
            var eventType = doc.RootElement.GetProperty("event_type").GetString() ?? "";
            var resource = doc.RootElement.GetProperty("resource");

            if (eventType == "PAYMENT.SALE.COMPLETED")
            {
                // Recurring payment received — find org by subscription ID
                var billingAgreementId = resource.TryGetProperty("billing_agreement_id", out var ba) ? ba.GetString() : null;
                if (!string.IsNullOrEmpty(billingAgreementId))
                {
                    var org = await _db.Organizations.FirstOrDefaultAsync(o => o.PayPalSubscriptionId == billingAgreementId);
                    if (org != null)
                    {
                        var amount = resource.TryGetProperty("amount", out var amt) && amt.TryGetProperty("total", out var tot)
                            ? decimal.TryParse(tot.GetString(), out var d) ? d : 0 : 0;
                        org.SubscriptionNextBillingDate = DateTime.UtcNow.AddMonths(1);
                        _db.PaymentRecords.Add(new PaymentRecord
                        {
                            OrganizationId = org.Id,
                            PaymentType = "subscription",
                            Amount = amount,
                            Status = "succeeded",
                            PayPalSubscriptionId = billingAgreementId,
                            Description = $"Recurring monthly payment received",
                            PlanKey = org.Plan.ToString()
                        });
                        _db.ActivityLogs.Add(new ActivityLog
                        {
                            Action = "recurring_payment_received",
                            Description = $"Monthly recurring payment received (PayPal: {billingAgreementId}). Next billing: {org.SubscriptionNextBillingDate:yyyy-MM-dd}.",
                            OrganizationId = org.Id
                        });
                        await _db.SaveChangesAsync();
                    }
                }
            }
            else if (eventType == "BILLING.SUBSCRIPTION.CANCELLED" || eventType == "BILLING.SUBSCRIPTION.SUSPENDED")
            {
                var subId = resource.GetProperty("id").GetString();
                if (!string.IsNullOrEmpty(subId))
                {
                    var org = await _db.Organizations.FirstOrDefaultAsync(o => o.PayPalSubscriptionId == subId);
                    if (org != null)
                    {
                        org.SubscriptionStatus = eventType.Contains("CANCELLED") ? "CANCELLED" : "SUSPENDED";
                        _db.ActivityLogs.Add(new ActivityLog
                        {
                            Action = "subscription_" + (eventType.Contains("CANCELLED") ? "cancelled" : "suspended"),
                            Description = $"Subscription {org.SubscriptionStatus.ToLower()} by PayPal webhook ({subId}).",
                            OrganizationId = org.Id
                        });
                        await _db.SaveChangesAsync();
                    }
                }
            }
            else if (eventType == "BILLING.SUBSCRIPTION.PAYMENT.FAILED")
            {
                var subId = resource.TryGetProperty("id", out var sid) ? sid.GetString() : null;
                if (!string.IsNullOrEmpty(subId))
                {
                    var org = await _db.Organizations.FirstOrDefaultAsync(o => o.PayPalSubscriptionId == subId);
                    if (org != null)
                    {
                        _db.PaymentRecords.Add(new PaymentRecord
                        {
                            OrganizationId = org.Id,
                            PaymentType = "subscription",
                            Amount = 0,
                            Status = "failed",
                            PayPalSubscriptionId = subId,
                            Description = "Recurring payment failed",
                            ErrorMessage = "PayPal billing subscription payment failed — org notified to fix payment within 5 days.",
                            PlanKey = org.Plan.ToString()
                        });
                        _db.ActivityLogs.Add(new ActivityLog
                        {
                            Action = "recurring_payment_failed",
                            Description = $"Recurring payment FAILED for org '{org.Name}' (PayPal: {subId}). Must fix payment within 5 days.",
                            OrganizationId = org.Id
                        });
                        await _db.SaveChangesAsync();
                    }
                }
            }
        }
        catch
        {
            // Log but always return 200 to PayPal
        }

        return Ok();
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

public class PayPalCreateSubscriptionRequest
{
    public int OrganizationId { get; set; }
    public string PlanKey { get; set; } = ""; // "Professional" or "Enterprise"
}

public class PayPalActivateSubscriptionRequest
{
    public int OrganizationId { get; set; }
    public string PlanKey { get; set; } = "";
}

public class PayPalCancelSubscriptionRequest
{
    public int OrganizationId { get; set; }
    public string? Reason { get; set; }
}
