using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ChatPortal2.Controllers;

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
        if (plan == null) return Ok(new { Plan = "Free", IsTrialActive = false, DaysRemaining = 0 });
        return Ok(new
        {
            plan.Plan,
            plan.IsTrialActive,
            plan.IsTrialExpired,
            plan.DaysRemaining,
            plan.HasUsedTrial,
            plan.TrialEndDate
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
