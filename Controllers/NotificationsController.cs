using AIInsights.Data;
using AIInsights.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.Controllers;

/// <summary>
/// Notification API consumed by the navbar bell widget.
/// A user receives a notification row if ANY of the following is true:
///   - notification.Scope == "All"
///   - notification.Scope == "Org" AND notification.OrganizationId == user.OrganizationId
///   - notification.Scope == "User" AND notification.TargetUserId == user.Id
/// The notification is filtered out once the user has dismissed it
/// (UserNotification.DismissedAt IS NOT NULL) or it has expired.
/// </summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db) { _db = db; }

    private string? CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private async Task<ApplicationUser?> GetUserAsync()
    {
        var id = CurrentUserId;
        if (string.IsNullOrEmpty(id)) return null;
        return await _db.Users.FindAsync(id);
    }

    /// <summary>
    /// Base query for the current user's visible (non-dismissed, non-expired) notifications.
    /// </summary>
    private IQueryable<Notification> BaseQueryFor(ApplicationUser user)
    {
        var now = DateTime.UtcNow;
        var orgId = user.OrganizationId;
        var uid = user.Id;

        return _db.Notifications
            .AsNoTracking()
            .Where(n =>
                (n.Scope == "All"
                 || (n.Scope == "Org" && n.OrganizationId != null && n.OrganizationId == orgId)
                 || (n.Scope == "User" && n.TargetUserId == uid))
                && (n.ExpiresAt == null || n.ExpiresAt > now)
                // Exclude dismissed
                && !_db.UserNotifications.Any(un =>
                        un.UserId == uid && un.NotificationId == n.Id && un.DismissedAt != null));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 30, [FromQuery] bool unreadOnly = false)
    {
        var user = await GetUserAsync();
        if (user == null) return Unauthorized();

        take = Math.Clamp(take, 1, 100);

        var q = BaseQueryFor(user);

        var rows = await q
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Body,
                n.Type,
                n.Severity,
                n.Link,
                n.CreatedAt,
                n.Scope,
                n.CreatedByRole,
                ReadAt = _db.UserNotifications
                    .Where(un => un.UserId == user.Id && un.NotificationId == n.Id)
                    .Select(un => un.ReadAt)
                    .FirstOrDefault()
            })
            .ToListAsync();

        // EF Core returns DateTime with Kind=Unspecified; force UTC so the JSON
        // carries a "Z" suffix and the browser does not treat it as local time.
        var joined = rows.Select(j => new
        {
            j.Id,
            j.Title,
            j.Body,
            j.Type,
            j.Severity,
            j.Link,
            CreatedAt = DateTime.SpecifyKind(j.CreatedAt, DateTimeKind.Utc),
            j.Scope,
            j.CreatedByRole,
            ReadAt = j.ReadAt.HasValue
                ? DateTime.SpecifyKind(j.ReadAt.Value, DateTimeKind.Utc)
                : (DateTime?)null
        }).ToList();

        if (unreadOnly)
            joined = joined.Where(j => j.ReadAt == null).ToList();

        return Ok(joined);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var user = await GetUserAsync();
        if (user == null) return Unauthorized();

        var count = await BaseQueryFor(user)
            .CountAsync(n => !_db.UserNotifications.Any(un =>
                un.UserId == user.Id && un.NotificationId == n.Id && un.ReadAt != null));

        return Ok(new { count });
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var user = await GetUserAsync();
        if (user == null) return Unauthorized();

        // Ensure the notification is actually visible to the user
        var exists = await BaseQueryFor(user).AnyAsync(n => n.Id == id);
        if (!exists) return NotFound();

        await UpsertStateAsync(user.Id, id, read: true, dismissed: false);
        return Ok(new { success = true });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var user = await GetUserAsync();
        if (user == null) return Unauthorized();

        var ids = await BaseQueryFor(user)
            .Select(n => n.Id)
            .ToListAsync();

        var existing = await _db.UserNotifications
            .Where(un => un.UserId == user.Id && ids.Contains(un.NotificationId))
            .ToListAsync();

        var existingMap = existing.ToDictionary(e => e.NotificationId);
        var now = DateTime.UtcNow;

        foreach (var nid in ids)
        {
            if (existingMap.TryGetValue(nid, out var un))
            {
                if (un.ReadAt == null) un.ReadAt = now;
            }
            else
            {
                _db.UserNotifications.Add(new UserNotification
                {
                    UserId = user.Id,
                    NotificationId = nid,
                    ReadAt = now
                });
            }
        }
        await _db.SaveChangesAsync();
        return Ok(new { success = true, count = ids.Count });
    }

    [HttpPost("{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id)
    {
        var user = await GetUserAsync();
        if (user == null) return Unauthorized();

        var exists = await BaseQueryFor(user).AnyAsync(n => n.Id == id);
        if (!exists) return NotFound();

        await UpsertStateAsync(user.Id, id, read: true, dismissed: true);
        return Ok(new { success = true });
    }

    private async Task UpsertStateAsync(string userId, int notificationId, bool read, bool dismissed)
    {
        var now = DateTime.UtcNow;
        var un = await _db.UserNotifications
            .FirstOrDefaultAsync(x => x.UserId == userId && x.NotificationId == notificationId);
        if (un == null)
        {
            un = new UserNotification
            {
                UserId = userId,
                NotificationId = notificationId,
                ReadAt = read ? now : null,
                DismissedAt = dismissed ? now : null
            };
            _db.UserNotifications.Add(un);
        }
        else
        {
            if (read && un.ReadAt == null) un.ReadAt = now;
            if (dismissed && un.DismissedAt == null) un.DismissedAt = now;
        }
        await _db.SaveChangesAsync();
    }
}
