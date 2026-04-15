using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.Controllers;

[Authorize]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly ISeoService _seoService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(AppDbContext db, ISeoService seoService, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _seoService = seoService;
        _userManager = userManager;
    }

    [HttpGet("/admin")]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || (user.Role != "OrgAdmin" && user.Role != "SuperAdmin"))
            return Redirect("/access-denied?statusCode=403");

        ViewBag.TotalOrgs = await _db.Organizations.CountAsync();
        ViewBag.TotalUsers = await _db.Users.CountAsync();
        ViewBag.TotalWorkspaces = await _db.Workspaces.CountAsync();
        ViewBag.TotalMessages = await _db.ChatMessages.CountAsync();
        return View();
    }

    [HttpGet("/admin/super")]
    public async Task<IActionResult> SuperAdmin()
    {
        // Verify caller is SuperAdmin
        var user = await _userManager.GetUserAsync(User);
        if (user == null || user.Role != "SuperAdmin")
            return Redirect("/access-denied?statusCode=403");

        ViewBag.TotalOrgs = await _db.Organizations.CountAsync();
        ViewBag.TotalUsers = await _db.Users.CountAsync();
        ViewBag.TotalWorkspaces = await _db.Workspaces.CountAsync();
        ViewBag.TotalMessages = await _db.ChatMessages.CountAsync();
        ViewBag.TotalAgents = await _db.Agents.CountAsync();
        ViewBag.TotalDatasources = await _db.Datasources.CountAsync();
        ViewBag.TotalReports = await _db.Reports.CountAsync();

        ViewBag.Organizations = await _db.Organizations
            .Select(o => new { o.Id, o.Name, UserCount = _db.Users.Count(u => u.OrganizationId == o.Id), o.CreatedAt })
            .ToListAsync();

        return View();
    }

    [HttpGet("/api/admin/super/stats")]
    public async Task<IActionResult> SuperStats()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || user.Role != "SuperAdmin")
            return Forbid();

        return Ok(new
        {
            totalOrgs = await _db.Organizations.CountAsync(),
            totalUsers = await _db.Users.CountAsync(),
            totalWorkspaces = await _db.Workspaces.CountAsync(),
            totalMessages = await _db.ChatMessages.CountAsync(),
            totalAgents = await _db.Agents.CountAsync(),
            totalDatasources = await _db.Datasources.CountAsync(),
            totalReports = await _db.Reports.CountAsync(),
            organizations = await _db.Organizations
                .Select(o => new
                {
                    o.Id,
                    o.Name,
                    userCount = _db.Users.Count(u => u.OrganizationId == o.Id),
                    workspaceCount = _db.Workspaces.Count(w => w.OrganizationId == o.Id),
                    o.CreatedAt
                })
                .ToListAsync()
        });
    }

    [HttpGet("/api/admin/super/users")]
    public async Task<IActionResult> SuperGetAllUsers()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null || user.Role != "SuperAdmin")
            return Forbid();

        var users = await _db.Users
            .Select(u => new { u.Id, u.FullName, u.Email, u.Role, u.Status, u.OrganizationId, u.CreatedAt })
            .ToListAsync();
        return Ok(users);
    }

    [HttpPut("/api/admin/super/users/{id}/role")]
    public async Task<IActionResult> SuperUpdateUserRole(string id, [FromBody] SuperUpdateRoleRequest req)
    {
        var caller = await _userManager.GetUserAsync(User);
        if (caller == null || caller.Role != "SuperAdmin")
            return Forbid();

        var target = await _userManager.FindByIdAsync(id);
        if (target == null) return NotFound();

        target.Role = req.Role ?? target.Role;
        await _userManager.UpdateAsync(target);
        return Ok(new { target.Id, target.Role });
    }
}

public class SuperUpdateRoleRequest
{
    public string? Role { get; set; }
}
