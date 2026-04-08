using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ChatPortal2.Models;

namespace ChatPortal2.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Datasource> Datasources => Set<Datasource>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<PinnedResult> PinnedResults => Set<PinnedResult>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<SeoEntry> SeoEntries => Set<SeoEntry>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Organization)
            .WithMany(o => o.Users)
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Subscription)
            .WithOne(s => s.User)
            .HasForeignKey<SubscriptionPlan>(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Workspace>()
            .HasOne(w => w.Organization)
            .WithMany(o => o.Workspaces)
            .HasForeignKey(w => w.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Agent>()
            .HasOne(a => a.Organization)
            .WithMany(o => o.Agents)
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Agent>()
            .HasOne(a => a.Datasource)
            .WithMany()
            .HasForeignKey(a => a.DatasourceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Datasource>()
            .HasOne(d => d.Organization)
            .WithMany(o => o.Datasources)
            .HasForeignKey(d => d.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SeoEntry>()
            .HasIndex(s => s.PageUrl)
            .IsUnique();

        builder.Entity<SubscriptionPlan>()
            .Ignore(s => s.IsTrialActive)
            .Ignore(s => s.IsTrialExpired)
            .Ignore(s => s.DaysRemaining);
    }
}
