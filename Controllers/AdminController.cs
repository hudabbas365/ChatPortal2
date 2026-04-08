using ChatPortal2.Data;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Controllers;

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

    [HttpGet("/admin/seo")]
    public IActionResult Seo() => View();
}
