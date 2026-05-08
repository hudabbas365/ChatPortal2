using AIInsights.Data;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.Controllers;

/// <summary>
/// Serves the full-page Power-Query-style Transform Workbench. The wizard
/// no longer authors transforms during workspace creation — users come back
/// to this page from the workspace home to build/edit pipelines and publish
/// changes that flow through to the dashboard.
/// </summary>
[Authorize]
public class TransformPageController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWorkspacePermissionService _permissions;

    public TransformPageController(AppDbContext db, IWorkspacePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    [HttpGet("/transform/{wsGuid}")]
    public async Task<IActionResult> Index(string wsGuid)
    {
        var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == wsGuid);
        if (ws == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var isOrgLevel = appUser?.Role == "OrgAdmin" || appUser?.Role == "SuperAdmin";

        if (!isOrgLevel && ws.OwnerId != userId &&
            !await _permissions.CanViewAsync(ws.Id, userId))
        {
            return Redirect("/access-denied?statusCode=403");
        }

        ViewBag.WorkspaceGuid = ws.Guid;
        ViewBag.WorkspaceName = ws.Name;
        ViewBag.WorkspaceType = ws.Type.ToString();
        return View();
    }
}
