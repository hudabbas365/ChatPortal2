namespace AIInsights.Models;

public class Organization
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? LogoUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ApplicationUser> Users { get; set; } = new();
    public List<Workspace> Workspaces { get; set; } = new();
    public List<Datasource> Datasources { get; set; } = new();
    public List<Agent> Agents { get; set; } = new();

    public PlanType Plan { get; set; } = PlanType.Free;
    public int EnterpriseExtraTokenPacks { get; set; } = 0; // Each pack = +2M tokens, $15
    public int PurchasedLicenses { get; set; } = 0; // Licenses bought by OrgAdmin, each allows 1 user assignment

    // ── PayPal Recurring Subscription ──
    public string? PayPalSubscriptionId { get; set; }
    public string? PayPalPlanId { get; set; }
    public string SubscriptionStatus { get; set; } = "NONE"; // NONE, APPROVAL_PENDING, ACTIVE, SUSPENDED, CANCELLED, EXPIRED
    public DateTime? SubscriptionStartDate { get; set; }
    public DateTime? SubscriptionNextBillingDate { get; set; }

    // ── Blocking ──
    public bool IsBlocked { get; set; } = false;
    public string? BlockedReason { get; set; }
    public DateTime? BlockedAt { get; set; }

    // ── Payment Records ──
    public List<PaymentRecord> PaymentRecords { get; set; } = new();

    public int MonthlyTokenBudget => Plan switch
    {
        PlanType.Enterprise    => 10_000_000 + (EnterpriseExtraTokenPacks * 2_000_000),
        PlanType.Professional  => 2_000_000,
        PlanType.FreeTrial     => 20_000,
        _                      => 0 // Free plan = no AI access
    };

    public int MaxWorkspaces => Plan switch
    {
        PlanType.Enterprise   => -1,  // unlimited
        PlanType.Professional => 10,
        _                     => 1
    };
}
