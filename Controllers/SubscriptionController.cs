using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AIInsights.Controllers;

[Authorize]
[Route("api/subscription")]
[ApiController]
public class SubscriptionController : ControllerBase
{
    private readonly SubscriptionService _subscriptionService;
    private readonly AppDbContext _db;

    public SubscriptionController(SubscriptionService subscriptionService, AppDbContext db)
    {
        _subscriptionService = subscriptionService;
        _db = db;
    }

    private async Task<bool> CanAccessUserAsync(string targetUserId)
    {
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (callerId == targetUserId) return true;
        var caller = await _db.Users.FindAsync(callerId);
        if (caller == null) return false;
        if (caller.Role == "SuperAdmin") return true;
        if (caller.Role == "OrgAdmin")
        {
            var target = await _db.Users.FindAsync(targetUserId);
            return target?.OrganizationId == caller.OrganizationId;
        }
        return false;
    }

    [HttpGet("{userId}")]
    public async Task<IActionResult> GetPlan(string userId)
    {
        if (!await CanAccessUserAsync(userId))
            return StatusCode(403, new { error = "You do not have permission to view this user's subscription." });

        var plan = await _subscriptionService.GetPlanAsync(userId);

        // Load the user's organization so we can surface org-level token add-ons
        // (EnterpriseExtraTokenPacks) to the client — the Billing tab needs this
        // to show the current extra pack count after a purchase.
        var user = await _db.Users.FindAsync(userId);
        var org = user?.OrganizationId != null ? await _db.Organizations.FindAsync(user.OrganizationId) : null;
        var extraPacks = org?.EnterpriseExtraTokenPacks ?? 0;

        // Feature gating is based on the USER's assigned license, not the org's
        // overall plan, so that a Pro-licensed user in an Enterprise org is
        // correctly blocked from AI Auto-Report and Explain-by-AI, and vice
        // versa. Fall back to the org plan if the user has no explicit plan.
        var effectivePlan = plan?.Plan ?? org?.Plan ?? PlanType.Free;
        var canAutoReport = PlanFeatures.AllowsAutoReport(effectivePlan);
        var canExplainAI  = PlanFeatures.AllowsChartExplain(effectivePlan);
        var monthlyBudget = org?.MonthlyTokenBudget ?? 0;
        var maxWorkspaces = org?.MaxWorkspaces ?? 1;

        if (plan == null)
        {
            return Ok(new
            {
                Plan = "Free",
                IsTrialActive = false,
                DaysRemaining = 0,
                EnterpriseExtraTokenPacks = extraPacks,
                CanUseAiReportGeneration = canAutoReport,
                CanUseAiChartExplain = canExplainAI,
                MonthlyTokenBudget = monthlyBudget,
                MaxWorkspaces = maxWorkspaces
            });
        }

        return Ok(new
        {
            // Return the plan as its string name (e.g. "Professional") so the
            // client never has to guess the numeric enum value. The enum
            // ordering is `Free, FreeTrial, Professional, Enterprise` which
            // is NOT the same as the UI's badge indices — serialising as a
            // string avoids mismatches like a Pro user showing as Enterprise.
            Plan = plan.Plan.ToString(),
            plan.IsTrialActive,
            plan.IsTrialExpired,
            plan.DaysRemaining,
            plan.HasUsedTrial,
            plan.TrialEndDate,
            EnterpriseExtraTokenPacks = extraPacks,
            CanUseAiReportGeneration = canAutoReport,
            CanUseAiChartExplain = canExplainAI,
            MonthlyTokenBudget = monthlyBudget,
            MaxWorkspaces = maxWorkspaces
        });
    }

    [HttpPost("activate-trial")]
    public async Task<IActionResult> ActivateTrial([FromBody] TrialRequest req)
    {
        if (!await CanAccessUserAsync(req.UserId ?? ""))
            return StatusCode(403, new { error = "You do not have permission to activate a trial for this user." });

        try
        {
            var plan = await _subscriptionService.ActivateTrialAsync(req.UserId ?? "");
            return Ok(new { success = true, plan.DaysRemaining });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("upgrade")]
    public async Task<IActionResult> Upgrade([FromBody] UpgradeRequest req)
    {
        if (!await CanAccessUserAsync(req.UserId ?? ""))
            return StatusCode(403, new { error = "You do not have permission to upgrade this user's plan." });

        var plan = await _subscriptionService.UpgradeAsync(req.UserId ?? "", req.Plan);
        return Ok(new { success = true, plan.Plan });
    }
}

public class TrialRequest
{
    public string? UserId { get; set; }
}

public class UpgradeRequest
{
    public string? UserId { get; set; }
    public PlanType Plan { get; set; }
}
