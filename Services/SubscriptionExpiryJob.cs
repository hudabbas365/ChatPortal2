using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.Services;

/// <summary>
/// Background sweep that downgrades organizations whose paid subscription has
/// fully ended:
///   • SubscriptionStatus = CANCELLED and the paid period (NextBillingDate)
///     has elapsed → revert Plan to Free, mark status EXPIRED.
///   • SubscriptionStatus = PAST_DUE and the GraceUntil window has elapsed
///     → revert Plan to Free, mark status EXPIRED.
/// Runs every 6 hours; idempotent.
/// </summary>
public class SubscriptionExpiryJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionExpiryJob> _logger;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);

    public SubscriptionExpiryJob(IServiceScopeFactory scopeFactory, ILogger<SubscriptionExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger the first run a little so we don't run during app warm-up.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubscriptionExpiryJob sweep failed.");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var changed = 0;

        // 1. Cancelled subscriptions whose paid period has elapsed.
        var cancelled = await db.Organizations
            .Where(o => o.SubscriptionStatus == "CANCELLED"
                        && o.Plan != PlanType.Free
                        && o.SubscriptionNextBillingDate != null
                        && o.SubscriptionNextBillingDate < now)
            .ToListAsync(ct);

        foreach (var org in cancelled)
        {
            var prev = org.Plan;
            org.Plan = PlanType.Free;
            org.SubscriptionStatus = "EXPIRED";
            db.ActivityLogs.Add(new ActivityLog
            {
                Action = "subscription_expired",
                Description = $"Cancelled subscription period ended; plan reverted from {prev} to Free.",
                OrganizationId = org.Id
            });
            changed++;
        }

        // 2. Past-due subscriptions whose grace window has elapsed.
        var pastDue = await db.Organizations
            .Where(o => o.SubscriptionStatus == "PAST_DUE"
                        && o.Plan != PlanType.Free
                        && o.GraceUntil != null
                        && o.GraceUntil < now)
            .ToListAsync(ct);

        foreach (var org in pastDue)
        {
            var prev = org.Plan;
            org.Plan = PlanType.Free;
            org.SubscriptionStatus = "EXPIRED";
            db.ActivityLogs.Add(new ActivityLog
            {
                Action = "subscription_expired_unpaid",
                Description = $"Grace period elapsed after failed recurring payments; plan reverted from {prev} to Free.",
                OrganizationId = org.Id
            });
            changed++;
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("SubscriptionExpiryJob downgraded {Count} organizations.", changed);
        }
    }
}
