using ChatPortal2.Data;
using ChatPortal2.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Services;

public interface ITrialEnforcementService
{
    Task<bool> CanCreateWorkspaceAsync(string userId);
    Task<bool> CanUseAiInsightsAsync(string userId);
    Task<bool> CanUseChatAsync(string userId);
}

public class TrialEnforcementService : ITrialEnforcementService
{
    private readonly AppDbContext _db;

    public TrialEnforcementService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CanCreateWorkspaceAsync(string userId) => await IsAllowedAsync(userId);
    public async Task<bool> CanUseAiInsightsAsync(string userId) => await IsAllowedAsync(userId);
    public async Task<bool> CanUseChatAsync(string userId) => await IsAllowedAsync(userId);

    private async Task<bool> IsAllowedAsync(string userId)
    {
        var plan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (plan == null) return false;

        return plan.Plan switch
        {
            PlanType.Professional => true,
            PlanType.Enterprise => true,
            PlanType.FreeTrial => plan.IsTrialActive,
            _ => false
        };
    }
}
