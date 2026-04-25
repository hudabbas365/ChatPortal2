using AIInsights.Data;
using AIInsights.Models;
using System.Security.Claims;

namespace AIInsights.Services;

/// <summary>
/// Scoped service for writing ActivityLog rows. When the current user is acting under
/// an impersonation token (has "act:email" claim), the actor's email is automatically
/// appended to the description so every audit row is fully attributable.
/// </summary>
public class ActivityLogger
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ActivityLogger(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>Appends an ActivityLog entry, annotating the description if impersonation is active.</summary>
    public void Log(string action, string description, string? userId = null, int? organizationId = null)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var actorEmail = user?.FindFirstValue("act:email");
        var annotatedDescription = !string.IsNullOrEmpty(actorEmail)
            ? $"{description} [impersonated by {actorEmail}]"
            : description;

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = action,
            Description = annotatedDescription,
            UserId = userId ?? "",
            OrganizationId = organizationId,
            CreatedAt = DateTime.UtcNow
        });
    }
}
