using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.Services;

public class TokenBudgetService : ITokenBudgetService
{
    private readonly AppDbContext _db;

    public TokenBudgetService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HasBudgetAsync(int organizationId)
    {
        var status = await GetStatusAsync(organizationId);
        return !status.IsExceeded;
    }

    public async Task<int> GetUsedTokensAsync(int organizationId, int year, int month)
    {
        return await _db.TokenUsages
            .Where(t => t.OrganizationId == organizationId && t.Year == year && t.Month == month)
            .SumAsync(t => t.TokensUsed);
    }

    public async Task RecordUsageAsync(int organizationId, string userId, int tokensUsed)
    {
        if (tokensUsed <= 0) return;

        var now = DateTime.UtcNow;
        _db.TokenUsages.Add(new TokenUsage
        {
            OrganizationId = organizationId,
            UserId = userId,
            TokensUsed = tokensUsed,
            Year = now.Year,
            Month = now.Month
        });
        await _db.SaveChangesAsync();
    }

    public async Task<TokenBudgetStatus> GetStatusAsync(int organizationId)
    {
        var now = DateTime.UtcNow;
        var org = await _db.Organizations.FindAsync(organizationId);
        if (org == null)
            return new TokenBudgetStatus { Used = 0, Budget = 0, Remaining = 0, IsExceeded = true };

        var budget = org.MonthlyTokenBudget; // Now plan-aware

        if (budget == 0)
            return new TokenBudgetStatus { Used = 0, Budget = 0, Remaining = 0, IsExceeded = true };

        var used = await GetUsedTokensAsync(organizationId, now.Year, now.Month);
        var remaining = Math.Max(0, budget - used);

        return new TokenBudgetStatus
        {
            Used = used,
            Budget = budget,
            Remaining = remaining,
            IsExceeded = used >= budget
        };
    }
}
