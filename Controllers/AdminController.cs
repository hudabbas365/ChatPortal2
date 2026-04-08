using ChatPortal2.Data;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Controllers;

[Authorize]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly ISeoService _seoService;

    public AdminController(AppDbContext db, ISeoService seoService)
    {
        _db = db;
        _seoService = seoService;
    }

    [HttpGet("/admin")]
    public async Task<IActionResult> Index()
    {
        ViewBag.TotalOrgs = await _db.Organizations.CountAsync();
        ViewBag.TotalUsers = await _db.Users.CountAsync();
        ViewBag.TotalWorkspaces = await _db.Workspaces.CountAsync();
        ViewBag.TotalMessages = await _db.ChatMessages.CountAsync();
        return View();
    }

    [HttpGet("/admin/super")]
    public async Task<IActionResult> SuperAdmin()
    {
        ViewBag.TotalOrgs = await _db.Organizations.CountAsync();
        ViewBag.TotalUsers = await _db.Users.CountAsync();
        ViewBag.TotalWorkspaces = await _db.Workspaces.CountAsync();
        ViewBag.TotalMessages = await _db.ChatMessages.CountAsync();
        ViewBag.TotalAgents = await _db.Agents.CountAsync();
        ViewBag.TotalDatasources = await _db.Datasources.CountAsync();
        return View();
    }
}
