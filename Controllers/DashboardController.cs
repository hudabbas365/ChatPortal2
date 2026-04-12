using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ChatPortal2.Data;
using System.Security.Claims;

namespace ChatPortal2.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IChartService _chartService;
    private readonly IDataService _dataService;
    private readonly AppDbContext _db;
    private readonly IWorkspacePermissionService _permissions;
    private const string SessionKey = "canvas_state";

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
    public async Task<IActionResult> Index([FromQuery] string? report, [FromQuery] string? workspace)
    {
        CanvasState canvas;

        // If a report guid is provided, load that report's canvas for editing
        if (!string.IsNullOrEmpty(report))
        {
            var rpt = await _db.Reports
                .Include(r => r.Datasource)
                .FirstOrDefaultAsync(r => r.Guid == report);
            if (rpt != null && !string.IsNullOrEmpty(rpt.CanvasJson))
            {
                canvas = JsonConvert.DeserializeObject<CanvasState>(rpt.CanvasJson) ?? new CanvasState();
                canvas.CanvasName = rpt.Name ?? canvas.CanvasName;
                ViewBag.ReportGuid = rpt.Guid;
                // Persist to session so chart operations work on this canvas
                HttpContext.Session.SetString(SessionKey, JsonConvert.SerializeObject(canvas));
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
                    HttpContext.Session.SetString(SessionKey, JsonConvert.SerializeObject(canvas));
                }
                else
                {
                    canvas = LoadOrCreateCanvas();
                }

                // Resolve datasource context for the workspace so charts can query real data
                var ds = await _db.Datasources
                    .Where(d => d.WorkspaceId == ws.Id)
                    .Select(d => new { d.Id, d.Name, d.Type })
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
        var canvasJson = HttpContext.Session.GetString(SessionKey);
        if (string.IsNullOrEmpty(canvasJson))
        {
            var canvas = new CanvasState { Charts = _chartService.GetDefaultCharts() };
            HttpContext.Session.SetString(SessionKey, JsonConvert.SerializeObject(canvas));
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
            canView = true
        });
    }
}

public class DashboardPermissionRequest
{
    public string? WorkspaceGuid { get; set; }
}
