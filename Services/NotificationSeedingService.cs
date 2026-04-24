using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.Services;

/// <summary>
/// Background worker that periodically converts system state (trial expiring/expired,
/// email-not-verified, org blocked, etc.) into auto-notifications delivered via the bell.
/// Uses <see cref="Notification.SystemKey"/> for dedupe so a given condition creates
/// the notification at most once.
/// </summary>
public class NotificationSeedingService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<NotificationSeedingService> _log;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public NotificationSeedingService(IServiceProvider sp, ILogger<NotificationSeedingService> log)
    {
        _sp = sp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Short initial delay so startup migrations/seeding can complete.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (TaskCanceledException) { return; }

        // Seed immediately at startup so system notifications (e.g. email-verify)
        // are available without waiting for the first periodic tick.
        try
        {
            await RunOnceAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Initial NotificationSeedingService run failed.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(Interval, stoppingToken); }
            catch (TaskCanceledException) { return; }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "NotificationSeedingService tick failed.");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var orgs = await db.Organizations.AsNoTracking().ToListAsync(ct);

        foreach (var org in orgs)
        {
            // Email not verified.
            if (!org.IsEmailVerified)
            {
                await UpsertAsync(db, $"email-verify-org-{org.Id}", n =>
                {
                    n.Scope = "Org";
                    n.OrganizationId = org.Id;
                    n.Title = "Verify your organization email";
                    n.Body = "Your organization email is not verified. Please check your inbox for the confirmation link.";
                    n.Type = "EmailVerify";
                    n.Severity = "high";
                    n.Link = "/org/settings";
                    n.CreatedByRole = "System";
                });
            }

            // Trial state — based on subscription records.
            var plan = org.Plan;
            var subStart = org.SubscriptionStartDate;

            if (plan == PlanType.FreeTrial)
            {
                // 30-day trial assumption — matches existing banner logic.
                var started = subStart ?? org.CreatedAt;
                var endsAt = started.AddDays(30);
                var daysLeft = (int)Math.Ceiling((endsAt - now).TotalDays);

                if (daysLeft <= 0)
                {
                    await UpsertAsync(db, $"trial-expired-org-{org.Id}", n =>
                    {
                        n.Scope = "Org";
                        n.OrganizationId = org.Id;
                        n.Title = "Your free trial has expired";
                        n.Body = "Upgrade now to continue using AI features without interruption.";
                        n.Type = "Trial";
                        n.Severity = "high";
                        n.Link = "/pricing";
                        n.CreatedByRole = "System";
                    });
                }
                else if (daysLeft <= 7)
                {
                    await UpsertAsync(db, $"trial-expiring-org-{org.Id}", n =>
                    {
                        n.Scope = "Org";
                        n.OrganizationId = org.Id;
                        n.Title = $"Free trial ends in {daysLeft} day{(daysLeft == 1 ? "" : "s")}";
                        n.Body = "Upgrade to Pro or Enterprise to keep your AI workflows running.";
                        n.Type = "Trial";
                        n.Severity = "normal";
                        n.Link = "/pricing";
                        n.CreatedByRole = "System";
                        n.ExpiresAt = endsAt;
                    });
                }
            }

            // Org blocked.
            if (org.IsBlocked)
            {
                await UpsertAsync(db, $"org-blocked-{org.Id}", n =>
                {
                    n.Scope = "Org";
                    n.OrganizationId = org.Id;
                    n.Title = "Organization is blocked";
                    n.Body = string.IsNullOrWhiteSpace(org.BlockedReason)
                        ? "Your organization has been blocked. Contact support."
                        : $"Your organization has been blocked. Reason: {org.BlockedReason}";
                    n.Type = "Warning";
                    n.Severity = "high";
                    n.CreatedByRole = "System";
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertAsync(AppDbContext db, string systemKey, Action<Notification> configure)
    {
        var existing = await db.Notifications.FirstOrDefaultAsync(n => n.SystemKey == systemKey);
        if (existing != null) return; // dedupe — do not spam.
        var n = new Notification { SystemKey = systemKey, CreatedAt = DateTime.UtcNow };
        configure(n);
        db.Notifications.Add(n);
    }
}
