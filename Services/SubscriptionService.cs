using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.Services;

public class SubscriptionService
{
    private readonly AppDbContext _db;

    public SubscriptionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<SubscriptionPlan?> GetPlanAsync(string userId)
    {
        return await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == userId);
    }

    public async Task<SubscriptionPlan> ActivateTrialAsync(string userId)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == userId);
        if (plan == null)
        {
            plan = new SubscriptionPlan { UserId = userId };
            _db.SubscriptionPlans.Add(plan);
        }

        if (plan.HasUsedTrial)
            throw new InvalidOperationException("Trial has already been used.");

        plan.Plan = PlanType.FreeTrial;
        plan.TrialStartDate = DateTime.UtcNow;
        plan.TrialEndDate = DateTime.UtcNow.AddDays(30);
        plan.HasUsedTrial = true;

        await _db.SaveChangesAsync();
        return plan;
    }

    public async Task<SubscriptionPlan> UpgradeAsync(string userId, PlanType planType)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == userId);
        if (plan == null)
        {
            plan = new SubscriptionPlan { UserId = userId };
            _db.SubscriptionPlans.Add(plan);
        }

        plan.Plan = planType;
        await _db.SaveChangesAsync();
        return plan;
    }

    public async Task<object> GetLimitsAsync(string userId)
    {
        var plan = await GetPlanAsync(userId);
        var planType = plan?.Plan ?? PlanType.Free;

        return planType switch
        {
            PlanType.Free => new { MaxWorkspaces = 1, MaxAgents = 1, MaxDatasources = 1, MaxMessages = 50, CanExport = false },
            PlanType.FreeTrial => new { MaxWorkspaces = 10, MaxAgents = 5, MaxDatasources = 5, MaxMessages = 1000, CanExport = true },
            PlanType.Professional => new { MaxWorkspaces = 50, MaxAgents = 20, MaxDatasources = 20, MaxMessages = 10000, CanExport = true },
            PlanType.Enterprise => new { MaxWorkspaces = -1, MaxAgents = -1, MaxDatasources = -1, MaxMessages = -1, CanExport = true },
            _ => new { MaxWorkspaces = 1, MaxAgents = 1, MaxDatasources = 1, MaxMessages = 50, CanExport = false }
        };
    }
}
