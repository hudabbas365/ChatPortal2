using ChatPortal2.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Controllers;

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
