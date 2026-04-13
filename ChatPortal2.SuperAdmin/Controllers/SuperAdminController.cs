using ChatPortal2.Data;
using ChatPortal2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChatPortal2.SuperAdmin.Controllers;

[Authorize]
public class SuperAdminController : Controller
{
    private readonly AppDbContext _db;

    public SuperAdminController(AppDbContext db)
    {
        _db = db;
    }

    private bool IsSuperAdmin() =>
        User.Claims.Any(c => c.Type == "role" && c.Value == "SuperAdmin");

    [HttpGet("/superadmin")]
    public async Task<IActionResult> Index()
    {
        if (!IsSuperAdmin()) return StatusCode(403);

        ViewBag.TotalOrgs = await _db.Organizations.CountAsync();
        ViewBag.TotalUsers = await _db.Users.CountAsync();
        ViewBag.TotalWorkspaces = await _db.Workspaces.CountAsync();
        ViewBag.TotalMessages = await _db.ChatMessages.CountAsync();
        return View("~/Views/Admin/Index.cshtml");
    }

    [HttpGet("/superadmin/organizations")]
    public async Task<IActionResult> Organizations()
    {
        if (!IsSuperAdmin()) return StatusCode(403);

        var orgs = await _db.Organizations
            .Include(o => o.Users)
            .Include(o => o.Workspaces)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return View("~/Views/Admin/Organizations.cshtml", orgs);
    }

    [HttpGet("/superadmin/activity")]
    public async Task<IActionResult> ActivityLogs([FromQuery] int page = 1)
    {
        if (!IsSuperAdmin()) return StatusCode(403);

        const int pageSize = 50;
        var logs = await _db.ActivityLogs
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        ViewBag.Page = page;
        return View("~/Views/Admin/ActivityLogs.cshtml", logs);
    }

    [HttpGet("/superadmin/aiconfig")]
    public IActionResult AiConfig()
    {
        if (!IsSuperAdmin()) return StatusCode(403);
        return View("~/Views/Admin/AiConfig.cshtml");
    }

    [HttpGet("/superadmin/payments")]
    public async Task<IActionResult> Payments()
    {
        if (!IsSuperAdmin()) return StatusCode(403);

        var users = await _db.Users
            .Where(u => u.CardLast4 != null)
            .Select(u => new { u.Id, u.FullName, u.Email, u.CardBrand, u.CardLast4 })
            .ToListAsync();
        return View("~/Views/Admin/Payments.cshtml", users);
    }

    [HttpGet("/superadmin/seo")]
    public async Task<IActionResult> Seo()
    {
        if (!IsSuperAdmin()) return StatusCode(403);

        var entries = await _db.SeoEntries.OrderBy(s => s.PageUrl).ToListAsync();
        return View("~/Views/Admin/Seo.cshtml", entries);
    }
}
