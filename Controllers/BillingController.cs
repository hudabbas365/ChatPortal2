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
    private readonly IEmailService _emailService;

    public BillingController(AppDbContext db, UserManager<ApplicationUser> userManager, IConfiguration config, IPayPalService payPal, IEmailService emailService)
    {
        _db = db;
        _userManager = userManager;
        _config = config;
        _payPal = payPal;
        _emailService = emailService;
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

    // Map raw PayPal error payloads to short, user-friendly messages.
    private static string FriendlyPayPalError(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Payment could not be completed. Please try again.";

        // Common cancel / abort cases
        if (raw.Contains("ORDER_NOT_APPROVED", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("PAYER_CANNOT_PAY", StringComparison.OrdinalIgnoreCase))
            return "Payment was cancelled before approval. You were not charged.";

        if (raw.Contains("ORDER_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase))
            return "This payment was already processed.";

        if (raw.Contains("INSTRUMENT_DECLINED", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("PAYER_ACCOUNT_RESTRICTED", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("TRANSACTION_REFUSED", StringComparison.OrdinalIgnoreCase))
            return "Your payment method was declined by PayPal. Please try a different card or account.";

        if (raw.Contains("INSUFFICIENT_FUNDS", StringComparison.OrdinalIgnoreCase))
            return "Insufficient funds. Please use a different payment method.";

        if (raw.Contains("AUTHENTICATION_FAILURE", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
            return "PayPal could not authenticate this request. Please try again.";

        // Try to pull a clean message from a JSON error body
        try
        {
            var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("message", out var m))
            {
                var msg = m.GetString();
                if (!string.IsNullOrWhiteSpace(msg)) return msg!;
            }
        }
        catch { }

        return "Payment could not be completed. Please try again or contact support.";
    }

    [HttpPost("/api/billing/subscribe")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req)
    {
        // ── B2 lock-down ──────────────────────────────────────────
        // This legacy endpoint upgraded a user's plan with NO payment
        // verification — anyone with a valid login could elevate themselves
        // to Enterprise. The real upgrade path is `/api/paypal/create-order`
        // + `/api/paypal/capture-order`, which verifies amount server-side.
        // Restricted to SuperAdmin so support staff can still manually adjust.
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

        var packs = req.Packs <= 0 ? 1 : req.Packs;
        org.EnterpriseExtraTokenPacks += packs; // +2M tokens each, $15 each — available to all plans
        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "token_pack_purchased",
            Description = $"{packs} token pack(s) purchased (+{packs * 2_000_000:N0} tokens).",
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

        // Check available licenses per type (Free assignments don't consume a license)
        if (planType != PlanType.Free)
        {
            var assignedOfType = await _db.SubscriptionPlans
                .CountAsync(s => s.User != null && s.User.OrganizationId == req.OrganizationId && s.Plan == planType);
            var purchasedOfType = planType == PlanType.Enterprise
                ? org.PurchasedEnterpriseLicenses
                : planType == PlanType.Professional
                    ? org.PurchasedProfessionalLicenses
                    : 0;
            // Check if this user already has this exact plan (re-assignment doesn't consume extra)
            var existingSub = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == req.UserId);
            var isReassignment = existingSub != null && existingSub.Plan == planType;
            if (!isReassignment && assignedOfType >= purchasedOfType)
                return BadRequest(new { error = $"No {planType} licenses available. You have {purchasedOfType} {planType} license(s) and {assignedOfType} already assigned. Buy more {planType} licenses first." });
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

        var assignedProfessional = await _db.SubscriptionPlans
            .CountAsync(s => s.User != null && s.User.OrganizationId == organizationId && s.Plan == PlanType.Professional);
        var assignedEnterprise = await _db.SubscriptionPlans
            .CountAsync(s => s.User != null && s.User.OrganizationId == organizationId && s.Plan == PlanType.Enterprise);
        var assignedCount = assignedProfessional + assignedEnterprise;

        var purchasedTotal = org.PurchasedProfessionalLicenses + org.PurchasedEnterpriseLicenses;

        return Ok(new
        {
            // Combined totals (backwards compatible)
            purchased = purchasedTotal,
            assigned = assignedCount,
            available = Math.Max(0, purchasedTotal - assignedCount),
            plan = org.Plan.ToString(),
            // Per-type breakdown
            proPurchased = org.PurchasedProfessionalLicenses,
            proAssigned = assignedProfessional,
            proAvailable = Math.Max(0, org.PurchasedProfessionalLicenses - assignedProfessional),
            enterprisePurchased = org.PurchasedEnterpriseLicenses,
            enterpriseAssigned = assignedEnterprise,
            enterpriseAvailable = Math.Max(0, org.PurchasedEnterpriseLicenses - assignedEnterprise),
            // Per-tier license subscription state (one PayPal recurring sub per tier).
            paypalProSubscriptionId = org.PayPalProSubscriptionId,
            paypalEntSubscriptionId = org.PayPalEntSubscriptionId,
            hasLicenseSubscription = !string.IsNullOrEmpty(org.PayPalProSubscriptionId) || !string.IsNullOrEmpty(org.PayPalEntSubscriptionId)
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
        var isEnterprise = string.Equals(req.Plan, "Enterprise", StringComparison.OrdinalIgnoreCase);
        if (isEnterprise) org.PurchasedEnterpriseLicenses += count;
        else org.PurchasedProfessionalLicenses += count;
        org.PurchasedLicenses += count; // keep legacy total in sync

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "licenses_purchased",
            Description = $"{count} {(isEnterprise ? "Enterprise" : "Professional")} license(s) purchased. Total: {org.PurchasedLicenses} (Pro: {org.PurchasedProfessionalLicenses}, Ent: {org.PurchasedEnterpriseLicenses}).",
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
            available = Math.Max(0, org.PurchasedLicenses - assignedCount),
            proPurchased = org.PurchasedProfessionalLicenses,
            enterprisePurchased = org.PurchasedEnterpriseLicenses
        });
    }

    // ── PayPal: Create Order ──────────────────────────────────────
    [HttpPost("/api/paypal/create-order")]
    public async Task<IActionResult> PayPalCreateOrder([FromBody] PayPalCreateOrderRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var baseUrl = (_config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
        // Return to a tiny anonymous page that just closes the popup — the
        // parent window detects the close and runs the capture step.
        var returnUrl = $"{baseUrl}/billing/paypal-return?result=success";
        var cancelUrl = $"{baseUrl}/billing/paypal-return?result=cancel";

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
            return StatusCode(500, new { error = FriendlyPayPalError(ex.Message) });
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
            return BadRequest(new { error = FriendlyPayPalError(capture.Error) });
        }

        // ── Server-side amount verification (B4) ──────────────────
        // The client sends PurchaseType / Quantity / PlanKey / TokenAmount.
        // We MUST recompute the expected price server-side and reject if the
        // captured amount doesn't match — otherwise a tampered client could
        // approve a $0.01 PayPal order and unlock an Enterprise license.
        var tokenAmt = req.TokenAmount > 0 ? req.TokenAmount : 1_000_000;
        var isEntExpected = string.Equals(req.PlanKey, "Enterprise", StringComparison.OrdinalIgnoreCase);
        decimal expectedAmount = req.PurchaseType switch
        {
            "license" => Math.Max(1, req.Quantity) * (isEntExpected ? 45m : 25m),
            "token_pack" => tokenAmt <= 100_000 ? 9m : tokenAmt <= 500_000 ? 20m : 25m,
            "plan_upgrade" => isEntExpected ? 45m : 25m,
            _ => 0m
        };
        if (expectedAmount <= 0)
            return BadRequest(new { error = "Unknown purchase type." });

        var currencyOk = string.Equals(capture.CapturedCurrency, "USD", StringComparison.OrdinalIgnoreCase);
        var amountOk = Math.Abs(capture.CapturedAmount - expectedAmount) <= 0.01m;
        if (!currencyOk || !amountOk)
        {
            _db.PaymentRecords.Add(new PaymentRecord
            {
                OrganizationId = req.OrganizationId,
                UserId = caller.Id,
                PaymentType = req.PurchaseType,
                Amount = capture.CapturedAmount,
                Currency = string.IsNullOrEmpty(capture.CapturedCurrency) ? "USD" : capture.CapturedCurrency,
                Status = "failed",
                PayPalOrderId = req.OrderId,
                Description = $"Amount mismatch on capture: {req.PurchaseType}",
                ErrorMessage = $"Captured {capture.CapturedAmount} {capture.CapturedCurrency}; expected {expectedAmount} USD.",
                PlanKey = req.PlanKey
            });
            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "payment_amount_mismatch",
                Description = $"Capture amount mismatch (order {req.OrderId}): captured {capture.CapturedAmount} {capture.CapturedCurrency}, expected {expectedAmount} USD. Purchase rejected.",
                UserId = caller.Id,
                OrganizationId = req.OrganizationId
            });
            await _db.SaveChangesAsync();
            return BadRequest(new { error = "Payment amount mismatch. The purchase has not been applied. Please contact support if you were charged." });
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
                var isEntLicense = string.Equals(req.PlanKey, "Enterprise", StringComparison.OrdinalIgnoreCase);
                if (isEntLicense) org.PurchasedEnterpriseLicenses += count;
                else org.PurchasedProfessionalLicenses += count;
                org.PurchasedLicenses += count; // keep legacy total in sync
                amount = count * (isEntLicense ? 45m : 25m);
                description = $"{count} {(isEntLicense ? "Enterprise" : "Professional")} license(s) purchased via PayPal (Order: {req.OrderId}).";
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
        var invoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{System.Security.Cryptography.RandomNumberGenerator.GetInt32(10000, 99999)}";
        int? qty = req.PurchaseType == "license" ? Math.Max(1, req.Quantity) : (req.PurchaseType == "token_pack" ? 1 : (int?)null);
        decimal? unitPrice = (qty.HasValue && qty.Value > 0) ? amount / qty.Value : (decimal?)null;
        long? tokensAdded = req.PurchaseType == "token_pack" ? (long?)(req.TokenAmount > 0 ? req.TokenAmount : 1_000_000) : null;
        var lineItems = new[]
        {
            new { description, quantity = qty ?? 1, unitPrice = unitPrice ?? amount, amount }
        };
        var paymentRecord = new PaymentRecord
        {
            OrganizationId = req.OrganizationId,
            UserId = caller.Id,
            PaymentType = req.PurchaseType,
            Amount = amount,
            Status = "succeeded",
            PayPalOrderId = req.OrderId,
            Description = description,
            PlanKey = req.PlanKey,
            InvoiceNumber = invoiceNumber,
            Quantity = qty,
            UnitPrice = unitPrice,
            Subtotal = amount,
            TokensAdded = tokensAdded,
            BillingName = caller.FullName,
            BillingEmail = caller.Email,
            BillingCompany = org.Name,
            LineItemsJson = System.Text.Json.JsonSerializer.Serialize(lineItems),
            PaidAt = DateTime.UtcNow
        };
        _db.PaymentRecords.Add(paymentRecord);
        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "paypal_payment_completed",
            Description = description,
            UserId = caller.Id,
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();

        // Send invoice email
        _ = _emailService.SendInvoiceEmailAsync(
            caller.Email!, caller.FullName, org.Name,
            description, amount, "USD",
            req.OrderId, DateTime.UtcNow);

        return Ok(new { success = true, message = description, invoiceNumber });
    }

    // ── Invoices: list all payment records for an organization ────
    [HttpGet("/api/billing/invoices")]
    public async Task<IActionResult> GetInvoices([FromQuery] int organizationId)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();
        if (caller.Role != "SuperAdmin" && caller.OrganizationId != organizationId)
            return StatusCode(403, new { error = "You do not have access to this organization's invoices." });

        var items = await _db.PaymentRecords
            .Where(p => p.OrganizationId == organizationId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id,
                invoiceNumber = p.InvoiceNumber ?? $"#{p.Id}",
                paymentType = p.PaymentType,
                description = p.Description,
                amount = p.Amount,
                currency = p.Currency,
                status = p.Status,
                quantity = p.Quantity,
                tokensAdded = p.TokensAdded,
                planKey = p.PlanKey,
                payPalOrderId = p.PayPalOrderId,
                createdAt = p.CreatedAt,
                paidAt = p.PaidAt
            })
            .ToListAsync();
        return Ok(items);
    }

    // ── Invoice detail (JSON) ─────────────────────────────────────
    [HttpGet("/api/billing/invoices/{id:int}")]
    public async Task<IActionResult> GetInvoice(int id)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();
        var p = await _db.PaymentRecords
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound(new { error = "Invoice not found." });
        if (caller.Role != "SuperAdmin" && caller.OrganizationId != p.OrganizationId)
            return StatusCode(403, new { error = "You do not have access to this invoice." });

        return Ok(new
        {
            id = p.Id,
            invoiceNumber = p.InvoiceNumber ?? $"#{p.Id}",
            organizationName = p.Organization?.Name,
            paymentType = p.PaymentType,
            paymentMethod = p.PaymentMethod,
            description = p.Description,
            amount = p.Amount,
            currency = p.Currency,
            status = p.Status,
            quantity = p.Quantity,
            unitPrice = p.UnitPrice,
            subtotal = p.Subtotal,
            taxAmount = p.TaxAmount,
            taxRegion = p.TaxRegion,
            taxRatePercent = p.TaxRatePercent,
            tokensAdded = p.TokensAdded,
            planKey = p.PlanKey,
            payPalOrderId = p.PayPalOrderId,
            payPalSubscriptionId = p.PayPalSubscriptionId,
            billingName = p.BillingName,
            billingEmail = p.BillingEmail,
            billingCompany = p.BillingCompany,
            billingAddressLine1 = p.BillingAddressLine1,
            billingAddressLine2 = p.BillingAddressLine2,
            billingCity = p.BillingCity,
            billingState = p.BillingState,
            billingPostalCode = p.BillingPostalCode,
            billingCountry = p.BillingCountry,
            lineItemsJson = p.LineItemsJson,
            createdAt = p.CreatedAt,
            paidAt = p.PaidAt
        });
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

        var baseUrl = (_config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
        // Return to a tiny anonymous page that just closes the popup — the
        // parent window detects the close and runs the activate step.
        var returnUrl = $"{baseUrl}/billing/paypal-return?result=success&kind=subscription";
        var cancelUrl = $"{baseUrl}/billing/paypal-return?result=cancel&kind=subscription";

        var isLicenseSub = string.Equals(req.PurchaseType, "license", StringComparison.OrdinalIgnoreCase);
        var isEnterpriseTier = string.Equals(req.PlanKey, "Enterprise", StringComparison.OrdinalIgnoreCase);
        var quantity = isLicenseSub ? Math.Max(1, req.Quantity) : 1;

        // Conversion flow: legacy org with one-time licenses switching to monthly
        // billing. Use existing seat count as the subscription quantity — admin
        // does NOT need to buy any new licenses.
        if (isLicenseSub && req.IsConversion)
        {
            var existingSeats = isEnterpriseTier ? org.PurchasedEnterpriseLicenses : org.PurchasedProfessionalLicenses;
            if (existingSeats <= 0)
                return BadRequest(new { error = $"No existing {req.PlanKey} licenses to convert." });
            quantity = existingSeats;
        }

        // For license subs, reject if the tier already holds an active subscription.
        // To change the seat count, cancel the existing tier sub first.
        if (isLicenseSub)
        {
            var existingTierId = isEnterpriseTier ? org.PayPalEntSubscriptionId : org.PayPalProSubscriptionId;
            if (!string.IsNullOrEmpty(existingTierId))
                return BadRequest(new { error = $"Your organization already has an active {req.PlanKey} license subscription. Cancel it first to change the seat count." });
        }

        try
        {
            // Reuse a single PayPal Product + Plan per tier (cached in PayPalService)
            // so we don't create new catalog objects on every checkout click.
            var plan = await _payPal.EnsureSubscriptionPlanAsync(req.PlanKey, monthlyPrice);
            if (!plan.Success)
                return BadRequest(new { error = plan.Error });

            // Create the subscription against the cached plan id (with quantity for license subs)
            var sub = await _payPal.CreateSubscriptionAsync(plan.PlanId, returnUrl, cancelUrl, quantity);
            if (!sub.Success)
                return BadRequest(new { error = sub.Error });

            if (isLicenseSub)
            {
                // Per-tier license subscription — store on the dedicated field, do
                // NOT mutate plan-level subscription state.
                if (isEnterpriseTier) org.PayPalEntSubscriptionId = sub.SubscriptionId;
                else org.PayPalProSubscriptionId = sub.SubscriptionId;

                _db.ActivityLogs.Add(new ActivityLog
                {
                    Action = req.IsConversion ? "license_subscription_conversion_created" : "license_subscription_created",
                    Description = req.IsConversion
                        ? $"License subscription conversion created (PayPal: {sub.SubscriptionId}, tier: {req.PlanKey}, existing seats: {quantity})."
                        : $"License subscription created (PayPal: {sub.SubscriptionId}, tier: {req.PlanKey}, quantity: {quantity}).",
                    UserId = caller.Id,
                    OrganizationId = req.OrganizationId
                });
                await _db.SaveChangesAsync();
                return Ok(new { subscriptionId = sub.SubscriptionId, approveUrl = sub.ApproveUrl, purchaseType = "license", tier = req.PlanKey, quantity, isConversion = req.IsConversion });
            }

            // Plan-level subscription — original flow.
            if (!string.IsNullOrEmpty(org.PayPalSubscriptionId) && org.PayPalSubscriptionId != sub.SubscriptionId)
            {
                _db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "subscription_replaced",
                    Description = $"Previous PayPal subscription {org.PayPalSubscriptionId} replaced with {sub.SubscriptionId} ({req.PlanKey}).",
                    UserId = caller.Id,
                    OrganizationId = req.OrganizationId
                });
            }

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

        // Resolve which subscription we're activating: a per-tier license sub
        // (matched by id or explicit Tier/PurchaseType) or the plan-level sub.
        bool isLicenseActivation = false;
        string? tierForLicense = null;
        string? subscriptionIdToUse = null;

        bool tryMatchTier(string? id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (!string.IsNullOrEmpty(org.PayPalProSubscriptionId) && org.PayPalProSubscriptionId == id) { tierForLicense = "Professional"; subscriptionIdToUse = id; isLicenseActivation = true; return true; }
            if (!string.IsNullOrEmpty(org.PayPalEntSubscriptionId) && org.PayPalEntSubscriptionId == id) { tierForLicense = "Enterprise"; subscriptionIdToUse = id; isLicenseActivation = true; return true; }
            return false;
        }

        if (!tryMatchTier(req.SubscriptionId))
        {
            if (string.Equals(req.PurchaseType, "license", StringComparison.OrdinalIgnoreCase))
            {
                tierForLicense = string.Equals(req.Tier, "Enterprise", StringComparison.OrdinalIgnoreCase) ? "Enterprise"
                                : string.Equals(req.Tier, "Professional", StringComparison.OrdinalIgnoreCase) ? "Professional"
                                : null;
                subscriptionIdToUse = tierForLicense == "Enterprise" ? org.PayPalEntSubscriptionId : org.PayPalProSubscriptionId;
                isLicenseActivation = !string.IsNullOrEmpty(subscriptionIdToUse);
            }
            else
            {
                subscriptionIdToUse = org.PayPalSubscriptionId;
            }
        }

        if (string.IsNullOrEmpty(subscriptionIdToUse))
            return BadRequest(new { error = "No pending subscription found." });

        try
        {
            // PayPal subscription state transitions APPROVAL_PENDING → APPROVED → ACTIVE
            // and there is a small propagation delay (often 1–5s in sandbox) between
            // the user finishing the approval popup and PayPal flipping the status.
            // Poll a few times so we don't return "still pending" prematurely.
            // If PayPal returns 404 the stored id is stale — clear it and tell the
            // caller, instead of looping for ~9s and reporting a false "still pending".
            PayPalSubscriptionDetails? details = null;
            string lastStatus = "";
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var (d, notFound) = await _payPal.TryGetSubscriptionDetailsAsync(subscriptionIdToUse);
                if (notFound)
                {
                    var staleId = subscriptionIdToUse;
                    if (isLicenseActivation)
                    {
                        if (tierForLicense == "Enterprise") org.PayPalEntSubscriptionId = null;
                        else org.PayPalProSubscriptionId = null;
                    }
                    else
                    {
                        org.PayPalSubscriptionId = null;
                        org.SubscriptionStatus = "NONE";
                        org.SubscriptionStartDate = null;
                        org.SubscriptionNextBillingDate = null;
                    }
                    _db.ActivityLogs.Add(new ActivityLog
                    {
                        Action = "subscription_pending_cleared_auto",
                        Description = $"Cleared stale APPROVAL_PENDING subscription on activate — PayPal returned 404 for {staleId}.",
                        UserId = caller.Id,
                        OrganizationId = req.OrganizationId
                    });
                    await _db.SaveChangesAsync();
                    return BadRequest(new { error = "This subscription no longer exists in PayPal. The pending state has been cleared — please start a new checkout.", cleared = true });
                }
                details = d;
                lastStatus = details?.Status ?? "";
                if (details != null && (lastStatus == "ACTIVE" || lastStatus == "APPROVED"))
                    break;
                if (attempt < 5)
                    await Task.Delay(1500);
            }

            if (details == null)
                return BadRequest(new { error = "Could not verify subscription with PayPal." });

            if (details.Status == "ACTIVE" || details.Status == "APPROVED")
            {
                // ── License-tier subscription activation ──
                if (isLicenseActivation)
                {
                    var tier = tierForLicense ?? (string.Equals(req.PlanKey, "Enterprise", StringComparison.OrdinalIgnoreCase) ? "Enterprise" : "Professional");
                    var qty = Math.Max(1, details.Quantity);
                    decimal unitPrice = string.Equals(tier, "Enterprise", StringComparison.OrdinalIgnoreCase) ? 45m : 25m;

                    // Conversion flow: existing seats are simply attached to the new
                    // recurring subscription — do NOT increment the license counts.
                    if (!req.IsConversion)
                    {
                        if (string.Equals(tier, "Enterprise", StringComparison.OrdinalIgnoreCase))
                        {
                            org.PurchasedEnterpriseLicenses += qty;
                        }
                        else
                        {
                            org.PurchasedProfessionalLicenses += qty;
                        }
                        org.PurchasedLicenses += qty; // legacy mirror
                    }

                    _db.ActivityLogs.Add(new ActivityLog
                    {
                        Action = req.IsConversion ? "license_subscription_converted" : "license_subscription_activated",
                        Description = req.IsConversion
                            ? $"{qty} existing {tier} license(s) converted to monthly subscription (PayPal: {subscriptionIdToUse}). Next billing: {details.NextBillingTime:yyyy-MM-dd}."
                            : $"{qty} {tier} license(s) added via recurring subscription (PayPal: {subscriptionIdToUse}). Next billing: {details.NextBillingTime:yyyy-MM-dd}.",
                        UserId = caller.Id,
                        OrganizationId = req.OrganizationId
                    });
                    var totalAmount = unitPrice * qty;
                    _db.PaymentRecords.Add(new PaymentRecord
                    {
                        OrganizationId = req.OrganizationId,
                        UserId = caller.Id,
                        PaymentType = req.IsConversion ? "license_subscription_conversion" : "license_subscription",
                        Amount = totalAmount,
                        Status = "succeeded",
                        PayPalSubscriptionId = subscriptionIdToUse,
                        Description = req.IsConversion
                            ? $"{qty} {tier} license(s) — converted to monthly subscription"
                            : $"{qty} {tier} license(s) — monthly subscription",
                        PlanKey = tier
                    });
                    await _db.SaveChangesAsync();

                    if (!string.IsNullOrEmpty(caller.Email))
                    {
                        _ = _emailService.SendInvoiceEmailAsync(
                            caller.Email, caller.FullName ?? caller.Email, org.Name,
                            req.IsConversion
                                ? $"{qty} {tier} license(s) — converted to monthly subscription"
                                : $"{qty} {tier} license(s) — monthly subscription activated",
                            totalAmount, "USD",
                            subscriptionIdToUse ?? "", DateTime.UtcNow);
                    }

                    return Ok(new
                    {
                        success = true,
                        status = "ACTIVE",
                        purchaseType = "license",
                        tier,
                        quantity = qty,
                        amount = totalAmount,
                        nextBilling = details.NextBillingTime
                    });
                }

                // ── Plan-level subscription activation (original flow) ──
                // Resolve the tier authoritatively from PayPal's plan_id (or the
                // plan_id we stored at create time) so we don't depend on the
                // client-provided planKey, which can be stale or wrong if the
                // user subscribed from a different page/tab.
                var resolvedPlanKey = req.PlanKey;
                var planIdForLookup = !string.IsNullOrEmpty(details.PlanId) ? details.PlanId : org.PayPalPlanId;
                if ((string.IsNullOrWhiteSpace(resolvedPlanKey) ||
                     !Enum.TryParse<PlanType>(resolvedPlanKey, true, out _)) &&
                    !string.IsNullOrEmpty(planIdForLookup) &&
                    _payPal.TryResolvePlanKeyFromPlanId(planIdForLookup, out var derived) &&
                    !string.IsNullOrWhiteSpace(derived))
                {
                    resolvedPlanKey = derived;
                }

                if (Enum.TryParse<PlanType>(resolvedPlanKey, true, out var planType))
                {
                    org.Plan = planType;
                }
                org.SubscriptionStatus = "ACTIVE";
                org.SubscriptionStartDate = DateTime.UtcNow;
                org.SubscriptionNextBillingDate = details.NextBillingTime ?? DateTime.UtcNow.AddMonths(1);

                _db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "subscription_activated",
                    Description = $"Monthly {resolvedPlanKey} subscription activated (PayPal: {org.PayPalSubscriptionId}). Next billing: {org.SubscriptionNextBillingDate:yyyy-MM-dd}.",
                    UserId = caller.Id,
                    OrganizationId = req.OrganizationId
                });
                // Log successful subscription payment
                decimal subPrice = string.Equals(resolvedPlanKey, "Enterprise", StringComparison.OrdinalIgnoreCase) ? 45m : 25m;
                _db.PaymentRecords.Add(new PaymentRecord
                {
                    OrganizationId = req.OrganizationId,
                    UserId = caller.Id,
                    PaymentType = "subscription",
                    Amount = subPrice,
                    Status = "succeeded",
                    PayPalSubscriptionId = org.PayPalSubscriptionId,
                    Description = $"Monthly {resolvedPlanKey} subscription activated",
                    PlanKey = resolvedPlanKey
                });
                await _db.SaveChangesAsync();

                // Send invoice email for the subscription activation
                if (!string.IsNullOrEmpty(caller.Email))
                {
                    _ = _emailService.SendInvoiceEmailAsync(
                        caller.Email, caller.FullName ?? caller.Email, org.Name,
                        $"Monthly {resolvedPlanKey} subscription — activated",
                        subPrice, "USD",
                        org.PayPalSubscriptionId ?? "", DateTime.UtcNow);
                }

                return Ok(new { success = true, status = "ACTIVE", plan = org.Plan.ToString(), planKey = resolvedPlanKey, nextBilling = org.SubscriptionNextBillingDate });
            }

            return Ok(new { success = false, status = details.Status, error = $"Subscription status is {details.Status}, not yet active." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = FriendlyPayPalError(ex.Message) });
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

        var isTierCancel = !string.IsNullOrWhiteSpace(req.Tier);
        var isEntTier = string.Equals(req.Tier, "Enterprise", StringComparison.OrdinalIgnoreCase);
        var subIdToCancel = isTierCancel
            ? (isEntTier ? org.PayPalEntSubscriptionId : org.PayPalProSubscriptionId)
            : org.PayPalSubscriptionId;

        if (string.IsNullOrEmpty(subIdToCancel))
            return BadRequest(new { error = "No active subscription to cancel." });

        try
        {
            var cancelResult = await _payPal.TryCancelSubscriptionAsync(subIdToCancel, req.Reason ?? "Customer requested cancellation");
            if (!cancelResult.Success)
            {
                // Self-heal: if PayPal says the subscription is already cancelled /
                // suspended / expired, or the id doesn't exist anymore (sandbox wipe,
                // stale row, etc.), reflect that locally instead of bouncing the user
                // with an opaque "Failed to cancel" error.
                if (cancelResult.AlreadyCancelled || cancelResult.NotFound)
                {
                    if (isTierCancel)
                    {
                        if (cancelResult.NotFound)
                        {
                            if (isEntTier) org.PayPalEntSubscriptionId = null;
                            else org.PayPalProSubscriptionId = null;
                        }
                        _db.ActivityLogs.Add(new ActivityLog
                        {
                            Action = "license_subscription_cancel_skipped",
                            Description = $"{req.Tier} license subscription was already terminated at PayPal ({cancelResult.ErrorName} / HTTP {cancelResult.StatusCode}). Local state synced; no PayPal action taken.",
                            UserId = caller.Id,
                            OrganizationId = req.OrganizationId
                        });
                    }
                    else
                    {
                        org.SubscriptionStatus = cancelResult.NotFound ? "NONE" : "CANCELLED";
                        if (cancelResult.NotFound)
                        {
                            org.PayPalSubscriptionId = null;
                            org.SubscriptionStartDate = null;
                            org.SubscriptionNextBillingDate = null;
                        }
                        _db.ActivityLogs.Add(new ActivityLog
                        {
                            Action = "subscription_cancel_skipped",
                            Description = $"Subscription was already terminated at PayPal ({cancelResult.ErrorName} / HTTP {cancelResult.StatusCode}). Local state synced.",
                            UserId = caller.Id,
                            OrganizationId = req.OrganizationId
                        });
                    }
                    await _db.SaveChangesAsync();
                    return Ok(new
                    {
                        success = true,
                        status = cancelResult.NotFound ? "NONE" : "CANCELLED",
                        tier = isTierCancel ? req.Tier : null,
                        message = cancelResult.NotFound
                            ? "Subscription no longer exists at PayPal — your account has been updated."
                            : "Subscription was already cancelled at PayPal — your account has been updated."
                    });
                }

                // Real failure — surface PayPal's reason so the user/admin can act.
                var reason = !string.IsNullOrWhiteSpace(cancelResult.ErrorMessage)
                    ? cancelResult.ErrorMessage
                    : !string.IsNullOrWhiteSpace(cancelResult.ErrorName)
                        ? cancelResult.ErrorName
                        : $"PayPal returned HTTP {cancelResult.StatusCode}.";
                return BadRequest(new
                {
                    error = $"Failed to cancel subscription with PayPal: {reason}",
                    paypalError = cancelResult.ErrorName,
                    paypalStatus = cancelResult.StatusCode
                });
            }

            DateTime? endsOn = null;
            try
            {
                var details = await _payPal.GetSubscriptionDetailsAsync(subIdToCancel);
                endsOn = details?.NextBillingTime;
            }
            catch { /* best-effort */ }

            if (isTierCancel)
            {
                _db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "license_subscription_cancelled",
                    Description = $"{req.Tier} license subscription auto-renewal cancelled (PayPal: {subIdToCancel}). Licenses remain active until {endsOn:yyyy-MM-dd}; access revoked after that date by the expiry job.",
                    UserId = caller.Id,
                    OrganizationId = req.OrganizationId
                });
                // Note: we keep PayPalProSubscriptionId / PayPalEntSubscriptionId set so the
                // SubscriptionExpiryJob can sweep them on the next-billing date and reduce
                // the purchased license counts. No refund — current period is honored.
            }
            else
            {
                org.SubscriptionStatus = "CANCELLED";
                endsOn = endsOn ?? org.SubscriptionNextBillingDate;
                _db.ActivityLogs.Add(new ActivityLog
                {
                    Action = "subscription_cancelled",
                    Description = $"Auto-renewal cancelled (PayPal: {subIdToCancel}). Plan and all assigned licenses remain active until {endsOn:yyyy-MM-dd}; subscription will be suspended after this date.",
                    UserId = caller.Id,
                    OrganizationId = req.OrganizationId
                });
            }
            await _db.SaveChangesAsync();

            var endsOnText = endsOn.HasValue ? endsOn.Value.ToString("yyyy-MM-dd") : "the end of the current billing period";
            var message = isTierCancel
                ? $"{req.Tier} license subscription cancelled. Your assigned licenses remain active until {endsOnText}; no refund is issued for the current billing period."
                : $"Subscription cancelled. Your plan and all assigned licenses remain active until {endsOnText}. Access will be suspended after this date unless you resubscribe.";
            return Ok(new
            {
                success = true,
                status = "CANCELLED",
                tier = isTierCancel ? req.Tier : null,
                nextBillingDate = endsOn,
                message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Cancel error: {ex.Message}" });
        }
    }

    // Clears a stuck APPROVAL_PENDING subscription (e.g. user closed the PayPal
    // popup before approving). Only allowed when the org is currently in
    // APPROVAL_PENDING state — never touches an ACTIVE subscription.
    [HttpPost("/api/paypal/clear-pending-subscription")]
    public async Task<IActionResult> PayPalClearPendingSubscription([FromBody] PayPalCancelSubscriptionRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();
        if (!IsOrgAdminOf(caller, req.OrganizationId))
            return StatusCode(403, new { error = "Only Organization Admins can manage subscriptions." });

        var org = await _db.Organizations.FindAsync(req.OrganizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        if (!string.Equals(org.SubscriptionStatus, "APPROVAL_PENDING", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Cannot clear — current status is {org.SubscriptionStatus}." });

        var staleSubId = org.PayPalSubscriptionId;

        // Best-effort: attempt to cancel the pending subscription on PayPal so
        // it doesn't linger in their dashboard. Ignore failures (it may already
        // be expired, or never approved at all).
        if (!string.IsNullOrEmpty(staleSubId))
        {
            try { await _payPal.CancelSubscriptionAsync(staleSubId, "Abandoned before approval"); }
            catch { /* swallow */ }
        }

        org.PayPalSubscriptionId = null;
        org.SubscriptionStatus = "NONE";
        org.SubscriptionStartDate = null;
        org.SubscriptionNextBillingDate = null;

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "subscription_pending_cleared",
            Description = $"Cleared abandoned APPROVAL_PENDING subscription (PayPal: {staleSubId ?? "none"}).",
            UserId = caller.Id,
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpGet("/api/paypal/subscription-status")]
    public async Task<IActionResult> GetSubscriptionStatus([FromQuery] int organizationId)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var org = await _db.Organizations.FindAsync(organizationId);
        if (org == null) return NotFound(new { error = "Organization not found." });

        // Self-heal: if the row is stuck on APPROVAL_PENDING but PayPal has
        // already activated the subscription, update the DB now so the banner
        // disappears on this page load without requiring any manual action.
        // Also: if PayPal returns 404 for the stored id (stale sandbox id,
        // wiped after a sandbox reset / client switch / seed data), clear the
        // pending row so the customer isn't permanently stuck on the
        // "Awaiting PayPal approval…" banner with no way to retry.
        if (org.SubscriptionStatus == "APPROVAL_PENDING" && !string.IsNullOrEmpty(org.PayPalSubscriptionId))
        {
            try
            {
                var (details, notFound) = await _payPal.TryGetSubscriptionDetailsAsync(org.PayPalSubscriptionId);
                if (notFound)
                {
                    var staleId = org.PayPalSubscriptionId;
                    org.PayPalSubscriptionId = null;
                    org.SubscriptionStatus = "NONE";
                    org.SubscriptionStartDate = null;
                    org.SubscriptionNextBillingDate = null;
                    _db.ActivityLogs.Add(new ActivityLog
                    {
                        Action = "subscription_pending_cleared_auto",
                        Description = $"Cleared stale APPROVAL_PENDING subscription — PayPal returned 404 for {staleId}.",
                        OrganizationId = org.Id
                    });
                    await _db.SaveChangesAsync();
                }
                else if (details != null && (details.Status == "ACTIVE" || details.Status == "APPROVED"))
                {
                    // Also fix the Plan field — when the row is APPROVAL_PENDING
                    // the org.Plan is usually still the pre-checkout value (Free /
                    // FreeTrial / a different paid tier), so flipping only
                    // SubscriptionStatus would leave the customer on the wrong
                    // plan. Resolve the tier from PayPal's plan_id.
                    var planIdForLookup = !string.IsNullOrEmpty(details.PlanId) ? details.PlanId : org.PayPalPlanId;
                    if (!string.IsNullOrEmpty(planIdForLookup) &&
                        _payPal.TryResolvePlanKeyFromPlanId(planIdForLookup, out var resolved) &&
                        Enum.TryParse<PlanType>(resolved, true, out var planType))
                    {
                        org.Plan = planType;
                    }

                    org.SubscriptionStatus = "ACTIVE";
                    org.SubscriptionStartDate ??= DateTime.UtcNow;
                    org.SubscriptionNextBillingDate = details.NextBillingTime ?? org.SubscriptionNextBillingDate ?? DateTime.UtcNow.AddMonths(1);
                    _db.ActivityLogs.Add(new ActivityLog
                    {
                        Action = "subscription_status_self_healed",
                        Description = $"Subscription status corrected from APPROVAL_PENDING to ACTIVE on status check ({org.PayPalSubscriptionId}, plan={org.Plan}).",
                        OrganizationId = org.Id
                    });
                    await _db.SaveChangesAsync();
                }
            }
            catch { /* best effort — don't break the status endpoint if PayPal is unreachable */ }
        }

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
    // PayPal POSTs subscription/payment events here. Anonymous because PayPal
    // doesn't authenticate via cookie/JWT — instead we cryptographically verify
    // the request via the standard PayPal webhook signature endpoint.
    [HttpPost("/api/paypal/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PayPalWebhook([FromServices] IWebHostEnvironment env)
    {
        // Read raw body once — required so we can hand the exact bytes to
        // PayPal's signature-verification endpoint.
        string body;
        using (var reader = new StreamReader(Request.Body))
            body = await reader.ReadToEndAsync();

        // Verify signature. In Development we tolerate failures (so local
        // ngrok testing works without a real WebhookId), but in any other
        // environment a failed verification is a hard 401 — this is what
        // closes the "anyone can POST" hole.
        var verified = await _payPal.VerifyWebhookSignatureAsync(Request.Headers, body);
        if (!verified && !env.IsDevelopment())
            return Unauthorized();

        try
        {
            var doc = JsonDocument.Parse(body);
            var eventType = doc.RootElement.GetProperty("event_type").GetString() ?? "";
            var eventId = doc.RootElement.TryGetProperty("id", out var eidEl) ? eidEl.GetString() : null;
            var resource = doc.RootElement.GetProperty("resource");

            // ── Idempotency (B5) ──────────────────────────────────
            // PayPal can replay the same webhook (network blips, manual resend
            // from dashboard). Without dedupe, recurring-payment branches would
            // double-credit org subscriptions / token packs. Short-circuit if
            // we've already persisted a PaymentRecord for this event.id.
            if (!string.IsNullOrEmpty(eventId) &&
                await _db.PaymentRecords.AnyAsync(p => p.PayPalEventId == eventId))
            {
                return Ok(new { idempotent = true });
            }

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
                        // Successful recurring charge → clear any past-due state.
                        org.FailedPaymentCount = 0;
                        org.GraceUntil = null;
                        if (org.SubscriptionStatus == "PAST_DUE" || org.SubscriptionStatus == "APPROVAL_PENDING") org.SubscriptionStatus = "ACTIVE";
                        _db.PaymentRecords.Add(new PaymentRecord
                        {
                            OrganizationId = org.Id,
                            PaymentType = "subscription",
                            Amount = amount,
                            Status = "succeeded",
                            PayPalSubscriptionId = billingAgreementId,
                            PayPalEventId = eventId,
                            Description = $"Recurring monthly payment received",
                            PlanKey = org.Plan.ToString(),
                            PaidAt = DateTime.UtcNow
                        });
                        _db.ActivityLogs.Add(new ActivityLog
                        {
                            Action = "recurring_payment_received",
                            Description = $"Monthly recurring payment received (PayPal: {billingAgreementId}). Next billing: {org.SubscriptionNextBillingDate:yyyy-MM-dd}.",
                            OrganizationId = org.Id
                        });
                        await _db.SaveChangesAsync();

                        // Email invoice to ALL OrgAdmins of the org (not just the first one).
                        var orgAdmins = await _db.Users
                            .Where(u => u.OrganizationId == org.Id && u.Role == "OrgAdmin" && !string.IsNullOrEmpty(u.Email))
                            .ToListAsync();
                        foreach (var orgAdmin in orgAdmins)
                        {
                            _ = _emailService.SendInvoiceEmailAsync(
                                orgAdmin.Email!, orgAdmin.FullName ?? orgAdmin.Email!, org.Name,
                                $"Monthly {org.Plan} subscription — recurring payment",
                                amount, "USD",
                                billingAgreementId, DateTime.UtcNow);
                        }
                    }
                }
            }
            else if (eventType == "BILLING.SUBSCRIPTION.ACTIVATED" || eventType == "BILLING.SUBSCRIPTION.UPDATED" || eventType == "BILLING.SUBSCRIPTION.RE-ACTIVATED")
            {
                // Safety net: if the popup-close → activate-subscription call missed the
                // brief APPROVAL_PENDING → ACTIVE window, PayPal will still fire this
                // event and we flip the org to ACTIVE here.
                var subId = resource.TryGetProperty("id", out var sid2) ? sid2.GetString() : null;
                if (!string.IsNullOrEmpty(subId))
                {
                    var org = await _db.Organizations.FirstOrDefaultAsync(o => o.PayPalSubscriptionId == subId);
                    if (org != null && org.SubscriptionStatus != "ACTIVE")
                    {
                        DateTime? nextBilling = null;
                        if (resource.TryGetProperty("billing_info", out var bi) &&
                            bi.TryGetProperty("next_billing_time", out var nbt) &&
                            DateTime.TryParse(nbt.GetString(), out var dt))
                        {
                            nextBilling = dt;
                        }

                        org.SubscriptionStatus = "ACTIVE";
                        if (org.SubscriptionStartDate == null || org.SubscriptionStartDate == default)
                            org.SubscriptionStartDate = DateTime.UtcNow;
                        org.SubscriptionNextBillingDate = nextBilling ?? org.SubscriptionNextBillingDate ?? DateTime.UtcNow.AddMonths(1);

                        _db.ActivityLogs.Add(new ActivityLog
                        {
                            Action = "subscription_activated_webhook",
                            Description = $"Subscription activated by PayPal webhook ({subId}). Next billing: {org.SubscriptionNextBillingDate:yyyy-MM-dd}.",
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
                        // Track consecutive failures + open a 5-day grace window.
                        // SubscriptionExpiryJob will downgrade Plan→Free when the
                        // window elapses; PlanFeatures gates can also short-circuit
                        // on SubscriptionStatus == "PAST_DUE" if desired.
                        org.FailedPaymentCount += 1;
                        org.SubscriptionStatus = "PAST_DUE";
                        if (org.GraceUntil == null || org.GraceUntil < DateTime.UtcNow)
                            org.GraceUntil = DateTime.UtcNow.AddDays(5);

                        _db.PaymentRecords.Add(new PaymentRecord
                        {
                            OrganizationId = org.Id,
                            PaymentType = "subscription",
                            Amount = 0,
                            Status = "failed",
                            PayPalSubscriptionId = subId,
                            PayPalEventId = eventId,
                            Description = $"Recurring payment failed (attempt {org.FailedPaymentCount})",
                            ErrorMessage = $"PayPal recurring payment failed. Grace period until {org.GraceUntil:yyyy-MM-dd}.",
                            PlanKey = org.Plan.ToString()
                        });
                        _db.ActivityLogs.Add(new ActivityLog
                        {
                            Action = "recurring_payment_failed",
                            Description = $"Recurring payment FAILED (attempt {org.FailedPaymentCount}) for org '{org.Name}' (PayPal: {subId}). Grace until {org.GraceUntil:yyyy-MM-dd}.",
                            OrganizationId = org.Id
                        });
                        await _db.SaveChangesAsync();

                        // Notify ALL OrgAdmins so payment can be fixed in time.
                        var orgAdmins = await _db.Users
                            .Where(u => u.OrganizationId == org.Id && u.Role == "OrgAdmin" && !string.IsNullOrEmpty(u.Email))
                            .ToListAsync();
                        foreach (var orgAdmin in orgAdmins)
                        {
                            _ = _emailService.SendInvoiceEmailAsync(
                                orgAdmin.Email!, orgAdmin.FullName ?? orgAdmin.Email!, org.Name,
                                $"Action required: payment failed for {org.Plan} subscription",
                                0, "USD",
                                subId, DateTime.UtcNow);
                        }
                    }
                }
            }
        }
        catch
        {
            // Log but always return 200 to PayPal so it doesn't keep retrying a poison message.
        }

        return Ok();
    }

    // ── PayPal popup return page ──────────────────────────────────
    // PayPal redirects the approval popup back to this URL. It requires no
    // authentication and simply closes the popup window so the parent tab
    // can run the capture / activate step. Anonymous because popup cookies
    // aren't always available after the cross-site PayPal redirect.
    [AllowAnonymous]
    [HttpGet("/billing/paypal-return")]
    public IActionResult PayPalReturn([FromQuery] string? result, [FromQuery] string? kind)
    {
        var safeResult = (result == "cancel") ? "cancel" : "success";
        var safeKind = (kind == "subscription") ? "subscription" : "payment";
        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"" />
    <title>PayPal — Returning to AI Insights 365</title>
    <style>
        body {{ font-family: 'Inter', system-ui, sans-serif; background: #f8f9fa; color: #333; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }}
        .card {{ background: #fff; padding: 2rem 2.5rem; border-radius: 12px; box-shadow: 0 4px 16px rgba(0,0,0,0.08); text-align: center; max-width: 420px; }}
        .icon {{ font-size: 48px; color: #2e7d32; margin-bottom: .5rem; }}
        .icon.cancel {{ color: #c62828; }}
        h1 {{ font-size: 1.1rem; margin: .25rem 0 .75rem; }}
        p {{ color: #666; font-size: .9rem; margin: 0; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""icon {(safeResult == "cancel" ? "cancel" : "")}"">{(safeResult == "cancel" ? "✕" : "✓")}</div>
        <h1>{(safeResult == "cancel" ? "Payment cancelled" : "Thank you!")}</h1>
        <p>{(safeResult == "cancel" ? "This window will close automatically." : "Finalizing your " + safeKind + "… this window will close automatically.")}</p>
    </div>
    <script>
        // Notify parent (best-effort) then close.
        try {{ if (window.opener && !window.opener.closed) {{ window.opener.postMessage({{ source: 'paypal-popup', result: '{safeResult}', kind: '{safeKind}' }}, '*'); }} }} catch (e) {{}}
        setTimeout(function() {{ try {{ window.close(); }} catch (e) {{}} }}, 600);
    </script>
</body>
</html>";
        return Content(html, "text/html");
    }

    // ── Printable invoice page ────────────────────────────────────
    [Authorize]
    [HttpGet("/billing/invoice/{id:int}")]
    public async Task<IActionResult> InvoiceView(int id)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();
        var p = await _db.PaymentRecords
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        if (caller.Role != "SuperAdmin" && caller.OrganizationId != p.OrganizationId)
            return StatusCode(403);
        return View("~/Views/Billing/Invoice.cshtml", p);
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
    public string Plan { get; set; } = "Professional"; // "Professional" or "Enterprise"
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
    public string? PurchaseType { get; set; } // "plan" (default) or "license"
    public int Quantity { get; set; } = 1;    // # of licenses when PurchaseType=="license"
    // True when converting existing one-time licenses into a recurring monthly
    // subscription. Quantity is taken from the org's existing seat count for
    // the tier; activation will NOT add new seats (just attaches the sub id).
    public bool IsConversion { get; set; }
}

public class PayPalActivateSubscriptionRequest
{
    public int OrganizationId { get; set; }
    public string PlanKey { get; set; } = "";
    public string? PurchaseType { get; set; } // "plan" or "license"
    public string? Tier { get; set; }         // "Professional" / "Enterprise" for license subs
    public string? SubscriptionId { get; set; }
    public bool IsConversion { get; set; }
}

public class PayPalCancelSubscriptionRequest
{
    public int OrganizationId { get; set; }
    public string? Reason { get; set; }
    public string? Tier { get; set; } // null=plan-level; "Professional"/"Enterprise" for license sub
}
