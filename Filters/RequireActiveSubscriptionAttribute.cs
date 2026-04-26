namespace AIInsights.Filters;

using System.Security.Claims;
using AIInsights.Data;
using AIInsights.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Blocks write actions (or any action it is applied to) when the caller's
/// organization no longer has an active paid subscription. OrgAdmins and
/// SuperAdmins always pass through.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireActiveSubscriptionAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) { await next(); return; }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId) as ApplicationUser;
        if (user == null) { await next(); return; }

        // SuperAdmin and OrgAdmin always pass through.
        if (user.Role == "SuperAdmin" || user.Role == "OrgAdmin") { await next(); return; }

        if (!user.OrganizationId.HasValue) { await next(); return; }
        var org = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == user.OrganizationId.Value);
        if (org == null) { await next(); return; }

        if (IsGated(org))
        {
            context.Result = new ObjectResult(new
            {
                error = "Your organization's subscription has ended. The portal is read-only until your Org Admin reactivates it.",
                code = "subscription_expired",
                status = org.SubscriptionStatus,
                plan = org.Plan.ToString()
            })
            {
                StatusCode = 403
            };
            return;
        }

        await next();
    }

    /// <summary>
    /// Returns true when the organization should be gated (read-only for regular users).
    /// </summary>
    public static bool IsGated(Organization org)
    {
        if (org.IsBlocked) return true;
        if (string.Equals(org.SubscriptionStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase)) return true;
        if (org.Plan == PlanType.Free &&
            !string.Equals(org.SubscriptionStatus, "APPROVAL_PENDING", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
