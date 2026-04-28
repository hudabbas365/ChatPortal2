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
                    // Restrict to OrgAdmins — only they can act on the email-verify
                    // flow from /org/settings.
                    n.TargetRolesCsv = "OrgAdmin";
                    n.Title = "Verify your organization email";
                    n.Body = "Your organization email is not verified. Please check your inbox for the confirmation link.";
                    n.Type = "EmailVerify";
                    n.Severity = "high";
                    n.Link = "/org/settings?tab=users";
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
                    // Trial fully ended — services are suspended automatically by
                    // TrialEnforcementService (IsTrialActive returns false once
                    // TrialEndDate has elapsed). We only surface the bell message here.
                    await UpsertAsync(db, $"trial-expired-org-{org.Id}", n =>
                    {
                        n.Scope = "Org";
                        n.OrganizationId = org.Id;
                        // Only OrgAdmins can purchase a subscription; show the
                        // alert to them so end-users aren't pushed to a page
                        // they can't act on.
                        n.TargetRolesCsv = "OrgAdmin";
                        n.Title = "Your free trial has expired";
                        n.Body = "All AI features have been suspended. Upgrade to Pro or Enterprise to restore service.";
                        n.Type = "Trial";
                        n.Severity = "urgent";
                        n.Link = "/org/settings?tab=users";
                        n.CreatedByRole = "System";
                    });
                }
                else
                {
                    // Daily countdown notification (days 1..30). The day number is
                    // baked into SystemKey so each day produces a fresh row; the
                    // previous day's row is auto-hidden via ExpiresAt = end of that
                    // day, keeping the bell list clean.
                    var endOfDay = now.Date.AddDays(1);
                    string title;
                    string body;
                    string severity;

                    if (daysLeft == 1)
                    {
                        title = "Free trial ends today";
                        body = "This is the last day of your free trial. All AI features will be suspended at the end of today unless you upgrade.";
                        severity = "urgent";
                    }
                    else if (daysLeft <= 7)
                    {
                        title = $"Free trial ends in {daysLeft} days";
                        body = "Upgrade to Pro or Enterprise to keep your AI workflows running.";
                        severity = "high";
                    }
                    else if (daysLeft <= 14)
                    {
                        title = $"Free trial: {daysLeft} days remaining";
                        body = "Your 30-day free trial is past the halfway mark. Upgrade any time to lock in continuous service.";
                        severity = "normal";
                    }
                    else
                    {
                        title = $"Free trial: {daysLeft} days remaining";
                        body = "Welcome to your 30-day free trial. We'll keep you posted as the end date approaches.";
                        severity = "low";
                    }

                    await UpsertAsync(db, $"trial-day-{daysLeft}-org-{org.Id}", n =>
                    {
                        n.Scope = "Org";
                        n.OrganizationId = org.Id;
                        // Only OrgAdmins can act on a subscription change.
                        n.TargetRolesCsv = "OrgAdmin";
                        n.Title = title;
                        n.Body = body;
                        n.Type = "Trial";
                        n.Severity = severity;
                        n.Link = "/org/settings?tab=users";
                        n.CreatedByRole = "System";
                        n.ExpiresAt = endOfDay;
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
                    n.TargetRolesCsv = "OrgAdmin";
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
        var now = DateTime.UtcNow;
        var n = new Notification
        {
            SystemKey = systemKey,
            CreatedAt = now,
            // System-seeded notifications are delivered immediately (no scheduled fan-out
            // worker is involved). The bell API filters on DeliveryStatus == "Delivered",
            // so without these two lines the rows stay "Pending" and never reach the
            // Org admin's notification list (e.g. "Verify your organization email").
            DeliveryStatus = "Delivered",
            DeliveredAt = now
        };
        configure(n);
        db.Notifications.Add(n);
    }
}
