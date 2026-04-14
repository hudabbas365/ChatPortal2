namespace ChatPortal2.Models;

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
    public int EnterpriseExtraTokenPacks { get; set; } = 0; // Each pack = +10M tokens, $10 one-time

    public int MonthlyTokenBudget => Plan switch
    {
        PlanType.Enterprise    => 10_000_000 + (EnterpriseExtraTokenPacks * 10_000_000),
        PlanType.Professional  => 2_000_000,
        PlanType.FreeTrial     => 500_000,
        _                      => 0 // Free plan = no AI access
    };

    public int MaxWorkspaces => Plan switch
    {
        PlanType.Enterprise   => -1,  // unlimited
        PlanType.Professional => 10,
        _                     => 1
    };
}
