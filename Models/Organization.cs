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
    public int PurchasedLicenses { get; set; } = 0; // Legacy total (kept for backwards compatibility). New code uses per-type counters below.
    public int PurchasedProfessionalLicenses { get; set; } = 0; // Professional licenses bought by OrgAdmin
    public int PurchasedEnterpriseLicenses { get; set; } = 0;   // Enterprise licenses bought by OrgAdmin

    // ── PayPal Recurring Subscription ──
    public string? PayPalSubscriptionId { get; set; }
    public string? PayPalPlanId { get; set; }
    public string SubscriptionStatus { get; set; } = "NONE"; // NONE, APPROVAL_PENDING, ACTIVE, SUSPENDED, CANCELLED, EXPIRED, PAST_DUE
    public DateTime? SubscriptionStartDate { get; set; }
    public DateTime? SubscriptionNextBillingDate { get; set; }

    // ── Recurring-payment grace tracking ──
    // Counts consecutive failed recurring charges. Reset to 0 on a successful
    // PAYMENT.SALE.COMPLETED webhook. When >= 1 a 5-day grace window is set
    // via GraceUntil; if it expires the SubscriptionExpiryJob downgrades the
    // plan to Free and marks status EXPIRED.
    public int FailedPaymentCount { get; set; } = 0;
    public DateTime? GraceUntil { get; set; }

    // ── Email Verification ──
    public bool IsEmailVerified { get; set; } = false;
    public string? EmailVerificationToken { get; set; }
    public DateTime? EmailVerificationTokenExpiry { get; set; }

    // ── Blocking ──
    public bool IsBlocked { get; set; } = false;
    public string? BlockedReason { get; set; }
    public DateTime? BlockedAt { get; set; }

    // ── Payment Records ──
    public List<PaymentRecord> PaymentRecords { get; set; } = new();

    public int MonthlyTokenBudget => Plan switch
    {
        PlanType.Enterprise    => 10_000_000 + (EnterpriseExtraTokenPacks * 2_000_000),
        PlanType.Professional  => 4_000_000  + (EnterpriseExtraTokenPacks * 2_000_000),
        PlanType.FreeTrial     => 2_000_000  + (EnterpriseExtraTokenPacks * 2_000_000),
        _                      => 0 // Free plan = no AI access
    };

    public int MaxWorkspaces => Plan switch
    {
        PlanType.Enterprise   => -1,  // unlimited
        PlanType.Professional => 10,
        _                     => 1
    };

    // ── Plan feature matrix ──
    // FreeTrial  : all features (evaluation)
    // Professional: chat + dashboards, but NO AI auto-report generation and NO "Explain by AI"
    // Enterprise : everything
    public bool CanUseAiReportGeneration => Plan == PlanType.FreeTrial || Plan == PlanType.Enterprise;
    public bool CanUseAiChartExplain     => Plan == PlanType.FreeTrial || Plan == PlanType.Enterprise;
}
