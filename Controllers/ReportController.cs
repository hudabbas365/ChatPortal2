using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Security.Claims;

namespace ChatPortal2.Controllers;

[Authorize]
public class ReportController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWorkspacePermissionService _permissions;

    public ReportController(AppDbContext db, IWorkspacePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    [AllowAnonymous]
    [HttpGet("/report/view/{guid}")]
    public async Task<IActionResult> ViewReport(string guid)
    {
        var report = await _db.Reports
            .Include(r => r.Workspace)
            .Include(r => r.Datasource)
            .Include(r => r.Agent)
            .FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        ViewBag.ReportGuid = report.Guid;
        ViewBag.ReportName = report.Name;
        ViewBag.CanvasJson = report.CanvasJson;
        ViewBag.WorkspaceGuid = report.Workspace?.Guid;
        ViewBag.WorkspaceName = report.Workspace?.Name;
        ViewBag.DatasourceName = report.Datasource?.Name;
        ViewBag.DatasourceId = report.DatasourceId;
        ViewBag.AgentName = report.Agent?.Name;
        ViewBag.Status = report.Status;

        return View("~/Views/Report/View.cshtml");
    }

    [HttpGet("/api/reports")]
    public async Task<IActionResult> GetByWorkspace([FromQuery] string workspaceGuid)
    {
        var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == workspaceGuid);
        if (ws == null) return NotFound();

        var reports = await _db.Reports
            .Where(r => r.WorkspaceId == ws.Id)
            .Include(r => r.Datasource)
            .Include(r => r.Agent)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.Guid,
                r.Name,
                r.Status,
                r.ChartIds,
                datasourceName = r.Datasource != null ? r.Datasource.Name : null,
                agentName = r.Agent != null ? r.Agent.Name : null,
                r.CreatedBy,
                r.CreatedAt
            })
            .ToListAsync();

        return Ok(reports);
    }

    [HttpGet("/api/reports/{guid}")]
    public async Task<IActionResult> GetByGuid(string guid)
    {
        var report = await _db.Reports
            .Include(r => r.Datasource)
            .Include(r => r.Agent)
            .Include(r => r.Workspace)
            .FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        return Ok(new
        {
            report.Id,
            report.Guid,
            report.Name,
            report.Status,
            report.ChartIds,
            report.CanvasJson,
            workspaceGuid = report.Workspace?.Guid,
            workspaceName = report.Workspace?.Name,
            datasourceId = report.DatasourceId,
            datasourceName = report.Datasource?.Name,
            datasourceType = report.Datasource?.Type,
            agentId = report.AgentId,
            agentName = report.Agent?.Name,
            report.CreatedBy,
            report.CreatedAt
        });
    }

    [HttpPost("/api/reports")]
    public async Task<IActionResult> Create([FromBody] CreateReportRequest? req)
    {
        if (req?.WorkspaceGuid == null) return BadRequest(new { error = "workspaceGuid is required." });

        var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == req.WorkspaceGuid);
        if (ws == null) return NotFound(new { error = "Workspace not found." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.CreatedBy ?? "";
        if (!await _permissions.CanEditAsync(ws.Id, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to create reports." });

        var report = new Report
        {
            Name = req.Name ?? "Untitled Report",
            WorkspaceId = ws.Id,
            DashboardId = req.DashboardId,
            DatasourceId = req.DatasourceId,
            AgentId = req.AgentId,
            ChartIds = req.ChartIds != null ? JsonConvert.SerializeObject(req.ChartIds) : null,
            CanvasJson = req.CanvasJson,
            Status = "Draft",
            CreatedBy = req.CreatedBy
        };

        _db.Reports.Add(report);
        await _db.SaveChangesAsync();

        return Ok(new { report.Id, report.Guid, report.Name, report.Status });
    }

    [HttpPut("/api/reports/{guid}")]
    public async Task<IActionResult> Update(string guid, [FromBody] UpdateReportRequest req)
    {
        var report = await _db.Reports.Include(r => r.Workspace).FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var wsId = report.WorkspaceId;
        if (wsId > 0 && !await _permissions.CanEditAsync(wsId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to update reports." });

        if (req.Name != null) report.Name = req.Name;
        if (req.ChartIds != null) report.ChartIds = JsonConvert.SerializeObject(req.ChartIds);
        if (req.CanvasJson != null) report.CanvasJson = req.CanvasJson;
        if (req.Status != null) report.Status = req.Status;

        await _db.SaveChangesAsync();
        return Ok(new { report.Id, report.Guid, report.Name, report.Status });
    }

    [HttpPost("/api/reports/{guid}/publish")]
    public async Task<IActionResult> Publish(string guid)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to publish reports." });

        report.Status = "Published";
        await _db.SaveChangesAsync();
        return Ok(new { report.Guid, report.Status });
    }

    [HttpDelete("/api/reports/{guid}")]
    public async Task<IActionResult> Delete(string guid)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanDeleteAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "Only Admins can delete reports." });

        _db.Reports.Remove(report);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}

public class CreateReportRequest
{
    public string? WorkspaceGuid { get; set; }
    public string? Name { get; set; }
    public int? DashboardId { get; set; }
    public int? DatasourceId { get; set; }
    public int? AgentId { get; set; }
    public List<string>? ChartIds { get; set; }
    public string? CanvasJson { get; set; }
    public string? CreatedBy { get; set; }
}

public class UpdateReportRequest
{
    public string? Name { get; set; }
    public List<string>? ChartIds { get; set; }
    public string? CanvasJson { get; set; }
    public string? Status { get; set; }
}
