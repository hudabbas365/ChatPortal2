using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChatPortal2.Controllers;

[Route("api/subscription")]
[ApiController]
public class SubscriptionController : ControllerBase
{
    private readonly SubscriptionService _subscriptionService;

    public SubscriptionController(SubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("{userId}")]
    public async Task<IActionResult> GetPlan(string userId)
    {
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
