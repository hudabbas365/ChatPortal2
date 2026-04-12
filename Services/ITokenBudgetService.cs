namespace ChatPortal2.Services;

public class TokenBudgetStatus
{
    public int Used { get; set; }
    public int Budget { get; set; }
    public int Remaining { get; set; }
    public bool IsExceeded { get; set; }
}

public interface ITokenBudgetService
{
    Task<bool> HasBudgetAsync(int organizationId);
    Task<int> GetUsedTokensAsync(int organizationId, int year, int month);
    Task RecordUsageAsync(int organizationId, string userId, int tokensUsed);
    Task<TokenBudgetStatus> GetStatusAsync(int organizationId);
}
