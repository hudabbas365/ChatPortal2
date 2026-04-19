using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using AIInsights.Data;
using System.Security.Claims;

namespace AIInsights.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IChartService _chartService;
    private readonly IDataService _dataService;
    private readonly AppDbContext _db;
    private readonly IWorkspacePermissionService _permissions;
    private const string SessionKeyPrefix = "canvas_state_";
    private const string SessionKeyLegacy = "canvas_state";

    private static readonly JsonSerializerSettings CamelCaseSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };

    public DashboardController(IChartService chartService, IDataService dataService, AppDbContext db, IWorkspacePermissionService permissions)
    {
        _chartService = chartService;
        _dataService = dataService;
        _db = db;
        _permissions = permissions;
    }

    [HttpGet("/dashboard")]
    public async Task<IActionResult> Index([FromQuery] string? report, [FromQuery] string? workspace, [FromQuery] string? agent)
    {
        CanvasState canvas;
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        // If a report guid is provided, load that report's canvas for editing
        if (!string.IsNullOrEmpty(report))
        {
            var rpt = await _db.Reports
                .Include(r => r.Datasource)
                .FirstOrDefaultAsync(r => r.Guid == report);
            if (rpt != null && !string.IsNullOrEmpty(rpt.CanvasJson))
            {
                // Permission check: verify user can access the report's workspace
                if (rpt.WorkspaceId > 0 && !await _permissions.CanViewAsync(rpt.WorkspaceId, currentUserId))
                {
                    var appUser = await _db.Users.FindAsync(currentUserId);
                    if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                        return Redirect("/access-denied?statusCode=403");
                }
                if (rpt.WorkspaceId > 0)
                {
                    var appUser2 = await _db.Users.FindAsync(currentUserId);
                    var wsRole = await _permissions.GetRoleAsync(rpt.WorkspaceId, currentUserId);
                    if (wsRole == "Viewer" && appUser2?.Role != "OrgAdmin" && appUser2?.Role != "SuperAdmin")
                        return Redirect("/access-denied?statusCode=403");
                }

                canvas = JsonConvert.DeserializeObject<CanvasState>(rpt.CanvasJson) ?? new CanvasState();
                canvas.CanvasName = rpt.Name ?? canvas.CanvasName;
                ViewBag.ReportGuid = rpt.Guid;
                // Persist to session so chart operations work on this canvas
                var sessionKey = SessionKeyPrefix + (report ?? "");
                HttpContext.Session.SetString(sessionKey, JsonConvert.SerializeObject(canvas));
                // Load datasource from the linked report
                if (rpt.Datasource != null)
                {
                    ViewBag.DatasourceId = rpt.Datasource.Id;
                    ViewBag.DatasourceName = rpt.Datasource.Name;
                    ViewBag.DatasourceType = rpt.Datasource.Type;
                }
                else if (rpt.DatasourceId.HasValue)
                {
                    var ds = await _db.Datasources
                        .Where(d => d.Id == rpt.DatasourceId.Value)
                        .Select(d => new { d.Id, d.Name, d.Type })
                        .FirstOrDefaultAsync();
                    if (ds != null)
                    {
                        ViewBag.DatasourceId = ds.Id;
                        ViewBag.DatasourceName = ds.Name;
                        ViewBag.DatasourceType = ds.Type;
                    }
                }
            }
            else
            {
                canvas = LoadOrCreateCanvas();
            }
        }
        // If a workspace guid is provided, find the latest report in that workspace
        else if (!string.IsNullOrEmpty(workspace))
        {
            var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == workspace);
            if (ws != null)
            {
                // Permission check: verify user can access this workspace
                if (!await _permissions.CanViewAsync(ws.Id, currentUserId))
                {
                    var appUser = await _db.Users.FindAsync(currentUserId);
                    if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                        return Redirect("/access-denied?statusCode=403");
                }
                var appUser2 = await _db.Users.FindAsync(currentUserId);
                var wsRole = await _permissions.GetRoleAsync(ws.Id, currentUserId);
                if (wsRole == "Viewer" && appUser2?.Role != "OrgAdmin" && appUser2?.Role != "SuperAdmin")
                    return Redirect("/access-denied?statusCode=403");

                // Find the most recent report in this workspace
                var latestReport = await _db.Reports
                    .Where(r => r.WorkspaceId == ws.Id && !string.IsNullOrEmpty(r.CanvasJson))
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync();

                if (latestReport != null)
                {
                    canvas = JsonConvert.DeserializeObject<CanvasState>(latestReport.CanvasJson!) ?? new CanvasState();
                    canvas.CanvasName = latestReport.Name ?? canvas.CanvasName;
                    ViewBag.ReportGuid = latestReport.Guid;
                    var sessionKey = SessionKeyPrefix + workspace;
                    HttpContext.Session.SetString(sessionKey, JsonConvert.SerializeObject(canvas));
                }
                else
                {
                    // Fresh canvas for this workspace — don't reuse another workspace's session state
                    canvas = new CanvasState { Charts = _chartService.GetDefaultCharts() };
                }

                // Resolve datasource context — prefer agent's bound datasource if ?agent= is provided
                Datasource? ds = null;
                if (!string.IsNullOrEmpty(agent))
                {
                    // Try to find agent by guid or id and use its datasource
                    var resolvedAgent = await _db.Agents.Include(a => a.Datasource)
                        .FirstOrDefaultAsync(a => a.Guid == agent || a.Id.ToString() == agent);
                    ds = resolvedAgent?.Datasource;
                }
                ds ??= await _db.Datasources
                    .Where(d => d.WorkspaceId == ws.Id)
                    .FirstOrDefaultAsync();
                if (ds != null)
                {
                    ViewBag.DatasourceId = ds.Id;
                    ViewBag.DatasourceName = ds.Name;
                    ViewBag.DatasourceType = ds.Type;
                }
                ViewBag.WorkspaceGuid = ws.Guid;
                ViewBag.WorkspaceName = ws.Name;
            }
            else
            {
                canvas = LoadOrCreateCanvas();
            }
        }
        else
        {
            canvas = LoadOrCreateCanvas();
        }

        ViewBag.InitialCharts = JsonConvert.SerializeObject(canvas.Charts, CamelCaseSettings);
        ViewBag.Pages = JsonConvert.SerializeObject(canvas.Pages, CamelCaseSettings);
        ViewBag.ActivePageIndex = canvas.ActivePageIndex;
        ViewBag.CanvasName = canvas.CanvasName;
        ViewBag.ChartLibrary = JsonConvert.SerializeObject(_chartService.GetGroupedCharts()
            .Select(g => new { group = g.Key, charts = g.ToList() }), CamelCaseSettings);
        ViewBag.Datasets = "[]";

        return View(canvas);
    }

    private CanvasState LoadOrCreateCanvas()
    {
        var canvasJson = HttpContext.Session.GetString(SessionKeyLegacy);
        if (string.IsNullOrEmpty(canvasJson))
        {
            var canvas = new CanvasState { Charts = _chartService.GetDefaultCharts() };
            HttpContext.Session.SetString(SessionKeyLegacy, JsonConvert.SerializeObject(canvas));
            return canvas;
        }
        return JsonConvert.DeserializeObject<CanvasState>(canvasJson) ?? new CanvasState();
    }

    [HttpPost("/api/dashboard/check-permission")]
    public async Task<IActionResult> CheckPermission([FromBody] DashboardPermissionRequest req)
    {
        if (string.IsNullOrEmpty(req.WorkspaceGuid))
            return BadRequest(new { error = "workspaceGuid is required." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var role = await _permissions.GetRoleByGuidAsync(req.WorkspaceGuid, userId);

        return Ok(new
        {
            role,
            canEdit = role == "Admin" || role == "Editor",
            canDelete = role == "Admin",
            canView = role != null
        });
    }
}

public class DashboardPermissionRequest
{
    public string? WorkspaceGuid { get; set; }
}
