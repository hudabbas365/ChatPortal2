using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.SuperAdmin.Services;

/// <summary>
/// Background service that polls every 30 seconds for scheduled notifications
/// whose ScheduleAt has passed, then fans out UserNotification rows and
/// optionally sends urgent emails.
/// </summary>
public class NotificationDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public NotificationDispatcher(IServiceScopeFactory scopeFactory, ILogger<NotificationDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchDueNotificationsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "NotificationDispatcher: unexpected error in dispatch loop.");
            }
        }
    }

    private async Task DispatchDueNotificationsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailer = scope.ServiceProvider.GetService<IUrgentNotificationEmailer>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var now = DateTime.UtcNow;

        var due = await db.Notifications
            .Where(n => n.DeliveryStatus == "Scheduled" && n.ScheduleAt <= now)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        _logger.LogInformation("NotificationDispatcher: dispatching {Count} scheduled notification(s).", due.Count);

        foreach (var notification in due)
        {
            try
            {
                await DispatchOneAsync(db, notification, emailer, config, _logger, ct, _scopeFactory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotificationDispatcher: failed to dispatch notification {Id}.", notification.Id);
            }
        }
    }

    /// <summary>
    /// Resolves recipients, creates UserNotification rows, updates delivery status, and sends urgent emails.
    /// </summary>
    public static async Task DispatchOneAsync(
        AppDbContext db,
        Notification notification,
        IUrgentNotificationEmailer? emailer,
        IConfiguration? config,
        ILogger? logger,
        CancellationToken ct,
        IServiceScopeFactory? scopeFactory = null)
    {
        try
        {
            var recipients = await ResolveRecipientsAsync(db, notification, ct);

            // De-duplicate
            var distinctIds = recipients.Select(r => r.Id).Distinct().ToHashSet();

            // Find already-existing UserNotification rows to avoid duplicates
            var existingIds = (await db.UserNotifications
                .Where(un => un.NotificationId == notification.Id)
                .Select(un => un.UserId)
                .ToListAsync(ct)).ToHashSet();

            var now = DateTime.UtcNow;
            var isUrgent = string.Equals(notification.Severity, "urgent", StringComparison.OrdinalIgnoreCase);

            var newRows = new List<UserNotification>();
            foreach (var uid in distinctIds)
            {
                if (existingIds.Contains(uid)) continue;
                newRows.Add(new UserNotification
                {
                    UserId = uid,
                    NotificationId = notification.Id,
                    EmailSent = false
                });
            }

            if (newRows.Count > 0)
                db.UserNotifications.AddRange(newRows);

            notification.DeliveryStatus = "Delivered";
            notification.DeliveredAt = now;

            await db.SaveChangesAsync(ct);

            // Capture the IDs of new rows for email fan-out
            var newRowData = newRows.Select(r => new { r.Id, r.UserId }).ToList();

            // Send urgent emails asynchronously — each fires in its own scope to avoid DbContext sharing
            if (isUrgent && emailer != null && newRowData.Count > 0)
            {
                var baseUrl = config?["AppBaseUrl"] ?? "";
                var recipientMap = recipients.ToDictionary(r => r.Id);
                var notificationId = notification.Id;
                var notificationTitle = notification.Title;
                var notificationBody = notification.Body;

                _ = Task.Run(async () =>
                {
                    foreach (var rowData in newRowData)
                    {
                        var user = recipientMap.TryGetValue(rowData.UserId, out var u) ? u : null;
                        var email = user?.Email ?? "";
                        var name = user?.FullName ?? "";
                        if (string.IsNullOrWhiteSpace(email)) continue;

                        var clickUrl = $"{baseUrl}/n/{rowData.Id}/click";
                        try
                        {
                            var sent = await emailer.SendAsync(email, name,
                                notificationTitle, notificationBody, clickUrl, CancellationToken.None);
                            if (sent && scopeFactory != null)
                            {
                                using var scope = scopeFactory.CreateScope();
                                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                var un = await scopedDb.UserNotifications.FindAsync(rowData.Id);
                                if (un != null)
                                {
                                    un.EmailSent = true;
                                    await scopedDb.SaveChangesAsync(CancellationToken.None);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.LogError(ex, "Failed to send urgent email for UserNotification {Id}.", rowData.Id);
                        }
                    }
                }, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "DispatchOneAsync failed for notification {Id}.", notification.Id);
            notification.DeliveryStatus = "Failed";
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Resolves the list of ApplicationUser recipients based on the notification's scope and targeting columns.
    /// </summary>
    public static async Task<List<AIInsights.Models.ApplicationUser>> ResolveRecipientsAsync(
        AppDbContext db, Notification notification, CancellationToken ct)
    {
        var scope = notification.Scope;

        if (scope.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return await db.Users.Cast<AIInsights.Models.ApplicationUser>().ToListAsync(ct);
        }

        if (scope.Equals("Org", StringComparison.OrdinalIgnoreCase) && notification.OrganizationId.HasValue)
        {
            var orgId = notification.OrganizationId.Value;
            return await db.Users.Cast<AIInsights.Models.ApplicationUser>()
                .Where(u => u.OrganizationId == orgId)
                .ToListAsync(ct);
        }

        if (scope.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            // Multi-user CSV
            if (!string.IsNullOrWhiteSpace(notification.TargetUserIdsCsv))
            {
                var ids = notification.TargetUserIdsCsv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet();
                return await db.Users.Cast<AIInsights.Models.ApplicationUser>()
                    .Where(u => ids.Contains(u.Id))
                    .ToListAsync(ct);
            }
            // Legacy single-user
            if (!string.IsNullOrWhiteSpace(notification.TargetUserId))
            {
                var u = await db.Users.FindAsync(new object[] { notification.TargetUserId }, ct)
                    as AIInsights.Models.ApplicationUser;
                return u != null ? new List<AIInsights.Models.ApplicationUser> { u } : new();
            }
        }

        if (scope.Equals("Role", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(notification.TargetRolesCsv))
        {
            var roles = notification.TargetRolesCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return await db.Users.Cast<AIInsights.Models.ApplicationUser>()
                .Where(u => roles.Contains(u.Role))
                .ToListAsync(ct);
        }

        return new List<AIInsights.Models.ApplicationUser>();
    }
}
