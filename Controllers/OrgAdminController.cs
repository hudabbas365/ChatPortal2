using ChatPortal2.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Controllers;

public class OrgAdminController : Controller
{
    private readonly AppDbContext _db;

    public OrgAdminController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/org/settings")]
    public IActionResult Settings() => View();
}
