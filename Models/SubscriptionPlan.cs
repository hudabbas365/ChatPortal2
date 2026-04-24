namespace AIInsights.Models;

public static class PlanPricing
{
    public const decimal ProPricePerUser = 25.00m;
    public const decimal EnterprisePricePerUser = 45.00m;
    public const decimal EnterpriseTokenPackPrice = 15.00m;

    // Token packages (available to all plans)
    public static readonly (string Name, int Tokens, decimal Price)[] TokenPackages = new[]
    {
        ("1M Tokens",  1_000_000,  15.00m),
        ("2M Tokens",  2_000_000,  20.00m),
        ("10M Tokens", 10_000_000, 25.00m)
    };
}

public enum PlanType { Free, FreeTrial, Professional, Enterprise }

// Centralised plan-feature matrix used by both server-side gates and API.
// Professional is the "chat + dashboards only" tier — AI Auto-Report and
// Explain-by-AI are Free Trial / Enterprise only.
public static class PlanFeatures
{
    public static bool AllowsAutoReport(PlanType p)   => p == PlanType.FreeTrial || p == PlanType.Enterprise;
    public static bool AllowsChartExplain(PlanType p) => p == PlanType.FreeTrial || p == PlanType.Enterprise;
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public PlanType Plan { get; set; } = PlanType.Free;
    public DateTime? TrialStartDate { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public bool HasUsedTrial { get; set; } = false;
    public bool IsTrialActive => Plan == PlanType.FreeTrial && TrialEndDate.HasValue && DateTime.UtcNow <= TrialEndDate.Value;
    public bool IsTrialExpired => HasUsedTrial && (!IsTrialActive);
    public int DaysRemaining => IsTrialActive ? Math.Max(0, (int)(TrialEndDate!.Value - DateTime.UtcNow).TotalDays) : 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ApplicationUser? User { get; set; }
}
