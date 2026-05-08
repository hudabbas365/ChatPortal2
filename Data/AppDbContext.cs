using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using AIInsights.Models;

namespace AIInsights.Data;

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
    public DbSet<WorkspaceUser> WorkspaceUsers => Set<WorkspaceUser>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<TokenUsage> TokenUsages => Set<TokenUsage>();
    public DbSet<WorkspaceMemory> WorkspaceMemories => Set<WorkspaceMemory>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<DocArticle> DocArticles => Set<DocArticle>();
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    public DbSet<SharedReport> SharedReports => Set<SharedReport>();
    public DbSet<ReportRevision> ReportRevisions => Set<ReportRevision>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<IntegrationHealthCheck> IntegrationHealthChecks => Set<IntegrationHealthCheck>();
    public DbSet<DigestRun> DigestRuns => Set<DigestRun>();
    public DbSet<PlanChangeLog> PlanChangeLogs => Set<PlanChangeLog>();
    public DbSet<TransformDraft> TransformDrafts => Set<TransformDraft>();
    public DbSet<TransformSource> TransformSources => Set<TransformSource>();
    public DbSet<TransformStep> TransformSteps => Set<TransformStep>();
    public DbSet<TransformRunAudit> TransformRunAudits => Set<TransformRunAudit>();

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

        builder.Entity<Workspace>()
            .HasOne(w => w.Owner)
            .WithMany()
            .HasForeignKey(w => w.OwnerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Organization>()
            .HasIndex(o => o.OrganizationGuid)
            .IsUnique();

        // Enforce unique organization Name at the DB level. The C# check in
        // AuthController.RegisterApi is racy (two concurrent registrations
        // can both pass AnyAsync and both succeed). SQL Server's default
        // collation is case-insensitive, so this index also guards against
        // "AIInsight365" vs "aiinsight365" duplicates.
        builder.Entity<Organization>()
            .HasIndex(o => o.Name)
            .IsUnique()
            .HasDatabaseName("IX_Organizations_Name_Unique");

        builder.Entity<Workspace>()
            .HasIndex(w => w.Guid)
            .IsUnique();

        builder.Entity<Agent>()
            .HasIndex(a => a.Guid)
            .IsUnique();

        builder.Entity<Datasource>()
            .HasIndex(d => d.Guid)
            .IsUnique();

        builder.Entity<Dashboard>()
            .HasIndex(d => d.Guid)
            .IsUnique();

        builder.Entity<Agent>()
            .HasOne(a => a.Organization)
            .WithMany(o => o.Agents)
            .HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Agent>()
            .HasOne(a => a.Datasource)
            .WithMany()
            .HasForeignKey(a => a.DatasourceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Agent>()
            .HasOne(a => a.Workspace)
            .WithMany()
            .HasForeignKey(a => a.WorkspaceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Dashboard>()
            .HasOne(d => d.Workspace)
            .WithMany()
            .HasForeignKey(d => d.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Dashboard>()
            .HasOne(d => d.Agent)
            .WithMany()
            .HasForeignKey(d => d.AgentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Dashboard>()
            .HasOne(d => d.Datasource)
            .WithMany()
            .HasForeignKey(d => d.DatasourceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Report>()
            .HasIndex(r => r.Guid)
            .IsUnique();

        builder.Entity<Report>()
            .HasOne(r => r.Workspace)
            .WithMany()
            .HasForeignKey(r => r.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Report>()
            .HasOne(r => r.Dashboard)
            .WithMany()
            .HasForeignKey(r => r.DashboardId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Report>()
            .HasOne(r => r.Datasource)
            .WithMany()
            .HasForeignKey(r => r.DatasourceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<Report>()
            .HasOne(r => r.Agent)
            .WithMany()
            .HasForeignKey(r => r.AgentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<TokenUsage>()
            .HasOne(t => t.Organization)
            .WithMany()
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Datasource>()
            .HasOne(d => d.Organization)
            .WithMany(o => o.Datasources)
            .HasForeignKey(d => d.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Datasource>()
            .HasOne(d => d.Workspace)
            .WithMany()
            .HasForeignKey(d => d.WorkspaceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<WorkspaceUser>()
            .HasOne(wu => wu.Workspace)
            .WithMany()
            .HasForeignKey(wu => wu.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<WorkspaceUser>()
            .HasOne(wu => wu.User)
            .WithMany()
            .HasForeignKey(wu => wu.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<WorkspaceUser>()
            .HasIndex(wu => new { wu.WorkspaceId, wu.UserId })
            .IsUnique();

     
        builder.Entity<SeoEntry>()
            .HasIndex(s => s.PageUrl)
            .IsUnique();

        // Webhook idempotency: dedupe replayed PayPal events by event.id (B5).
        builder.Entity<PaymentRecord>()
            .HasIndex(p => p.PayPalEventId)
            .IsUnique()
            .HasFilter("[PayPalEventId] IS NOT NULL");

        builder.Entity<SubscriptionPlan>()
            .Ignore(s => s.IsTrialActive)
            .Ignore(s => s.IsTrialExpired)
            .Ignore(s => s.DaysRemaining);

        builder.Entity<WorkspaceMemory>()
            .HasOne(m => m.Workspace)
            .WithMany()
            .HasForeignKey(m => m.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SharedReport>()
            .HasOne(sr => sr.Report)
            .WithMany()
            .HasForeignKey(sr => sr.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SharedReport>()
            .HasOne(sr => sr.User)
            .WithMany()
            .HasForeignKey(sr => sr.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SharedReport>()
            .HasIndex(sr => new { sr.ReportId, sr.UserId })
            .IsUnique();

        builder.Entity<ReportRevision>()
            .HasOne(rr => rr.Report)
            .WithMany()
            .HasForeignKey(rr => rr.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ReportRevision>()
            .HasIndex(rr => new { rr.ReportId, rr.Kind, rr.CreatedAt });

        builder.Entity<PlanChangeLog>()
            .HasOne(p => p.Organization)
            .WithMany()
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TransformDraft>()
            .HasIndex(d => d.Guid)
            .IsUnique();

        builder.Entity<TransformDraft>()
            .HasOne(d => d.Datasource)
            .WithMany()
            .HasForeignKey(d => d.DatasourceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<TransformSource>()
            .HasOne(s => s.TransformDraft)
            .WithMany(d => d.Sources)
            .HasForeignKey(s => s.TransformDraftId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TransformSource>()
            .HasOne(s => s.Datasource)
            .WithMany()
            .HasForeignKey(s => s.DatasourceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Entity<TransformSource>()
            .HasIndex(s => new { s.TransformDraftId, s.DatasourceId, s.Alias })
            .IsUnique();

        builder.Entity<TransformStep>()
            .HasOne(s => s.TransformDraft)
            .WithMany(d => d.Steps)
            .HasForeignKey(s => s.TransformDraftId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<TransformStep>()
            .HasIndex(s => new { s.TransformDraftId, s.SortOrder });

        builder.Entity<TransformRunAudit>()
            .HasIndex(a => a.RunGuid)
            .IsUnique();

        builder.Entity<TransformRunAudit>()
            .HasOne(a => a.Datasource)
            .WithMany()
            .HasForeignKey(a => a.DatasourceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<TransformRunAudit>()
            .HasOne(a => a.TransformDraft)
            .WithMany()
            .HasForeignKey(a => a.TransformDraftId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
