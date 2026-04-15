using AIInsights.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.Controllers;

[Authorize]
[Route("api/activity")]
[ApiController]
public class ActivityController : ControllerBase
{
    private readonly AppDbContext _db;

    public ActivityController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetActivity([FromQuery] int? organizationId, [FromQuery] string? userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var currentUser = await _db.Users.FindAsync(currentUserId);

        // Verify the requesting user belongs to the queried organization
        if (organizationId.HasValue)
        {
            if (currentUser?.OrganizationId != organizationId.Value)
            {
                if (currentUser?.Role != "SuperAdmin")
                    return StatusCode(403, new { error = "You do not have access to this organization's activity." });
            }
        }

        // When filtering by userId, only allow viewing own activity or same-org with OrgAdmin/SuperAdmin
        if (!string.IsNullOrEmpty(userId) && userId != currentUserId)
        {
            if (currentUser?.Role == "SuperAdmin")
            { /* allowed */ }
            else if (currentUser?.Role == "OrgAdmin")
            {
                var targetUser = await _db.Users.FindAsync(userId);
                if (targetUser?.OrganizationId != currentUser.OrganizationId)
                    return StatusCode(403, new { error = "You do not have access to this user's activity." });
            }
            else
            {
                return StatusCode(403, new { error = "You do not have access to this user's activity." });
            }
        }

        var query = _db.ActivityLogs.AsQueryable();
        if (organizationId.HasValue)
            query = query.Where(a => a.OrganizationId == organizationId.Value);
        else if (!string.IsNullOrEmpty(userId))
            query = query.Where(a => a.UserId == userId);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }
}
