using AIInsights.Data;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.SuperAdmin.Services;

public class OrganizationRetentionCleanupJob : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrganizationRetentionCleanupJob> _logger;

    public OrganizationRetentionCleanupJob(IServiceScopeFactory scopeFactory, ILogger<OrganizationRetentionCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); } catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OrganizationRetentionCleanupJob sweep failed.");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var retention = scope.ServiceProvider.GetRequiredService<OrganizationRetentionService>();
        var now = DateTime.UtcNow;
        var deleteCutoff = now.AddDays(-90);

        var expiredOrgs = await db.Organizations
            .Where(o => o.IsDeleted && o.DeactivatedAt != null && o.DeactivatedAt <= deleteCutoff)
            .OrderBy(o => o.DeactivatedAt)
            .ToListAsync(ct);

        var deleted = 0;
        foreach (var org in expiredOrgs)
        {
            await retention.SendPrePermanentDeletionEmailAsync(
                org,
                initiatedByUserId: "system",
                permanentDeleteAtUtc: now,
                immediateDeletion: true,
                ct);

            var (ok, _) = await retention.PermanentlyDeleteOrganizationAsync(
                org,
                initiatedByUserId: "system",
                source: "automatic_90_day_retention",
                ct);
            if (ok) deleted++;
        }

        if (deleted > 0)
            _logger.LogInformation("OrganizationRetentionCleanupJob permanently deleted {Count} organizations.", deleted);
    }
}
