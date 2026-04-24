using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Security.Claims;

namespace AIInsights.Controllers;

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

        // Anonymous users can only view published reports
        if (User.Identity?.IsAuthenticated != true && report.Status != "Published")
            return Redirect("/access-denied?statusCode=401");

        // Authenticated users must have workspace access, shared report access, or report must be Published
        string? workspaceRole = null;
        if (User.Identity?.IsAuthenticated == true && report.WorkspaceId > 0)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (report.Status != "Published")
            {
                var hasSharedAccess = await _db.SharedReports
                    .AnyAsync(sr => sr.ReportId == report.Id && sr.UserId == userId);
                if (!hasSharedAccess && !await _permissions.CanViewAsync(report.WorkspaceId, userId))
                {
                    var appUser = await _db.Users.FindAsync(userId);
                    if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                        return Redirect("/access-denied?statusCode=403");
                }
            }
            workspaceRole = await _permissions.GetRoleAsync(report.WorkspaceId, userId);
        }

        ViewBag.ReportGuid = report.Guid;
        ViewBag.ReportName = report.Name;
        ViewBag.CanvasJson = report.CanvasJson;
        ViewBag.WorkspaceGuid = report.Workspace?.Guid;
        ViewBag.WorkspaceName = report.Workspace?.Name;
        ViewBag.DatasourceName = report.Datasource?.Name;
        ViewBag.DatasourceId = report.DatasourceId;
        ViewBag.DatasourceType = report.Datasource?.Type;
        ViewBag.AgentName = report.Agent?.Name;
        ViewBag.Status = report.Status;
        ViewBag.UpdatedAt = report.UpdatedAt ?? report.CreatedAt;
        ViewBag.WorkspaceRole = workspaceRole;

        return View("~/Views/Report/View.cshtml");
    }

    [AllowAnonymous]
    [HttpGet("/report/share/{token}")]
    public async Task<IActionResult> AcceptShareLink(string token)
    {
        var report = await _db.Reports
            .Include(r => r.Workspace)
            .FirstOrDefaultAsync(r => r.ShareToken == token);
        if (report == null) return NotFound("Invalid or expired share link.");

        // If user is authenticated, grant them report-level access (not workspace-level)
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var already = await _db.SharedReports
                .AnyAsync(sr => sr.ReportId == report.Id && sr.UserId == userId);
            if (!already)
            {
                _db.SharedReports.Add(new SharedReport
                {
                    ReportId = report.Id,
                    UserId = userId
                });
                await _db.SaveChangesAsync();
            }
        }

        return Redirect("/report/view/" + report.Guid);
    }

    [HttpGet("/api/reports/{guid}/share")]
    public async Task<IActionResult> GetShareToken(string guid)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        return Ok(new { shareToken = report.ShareToken });
    }

    [HttpPost("/api/reports/{guid}/share")]
    public async Task<IActionResult> GenerateShareToken(string guid)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to share reports." });

        if (string.IsNullOrEmpty(report.ShareToken))
        {
            report.ShareToken = Guid.NewGuid().ToString("N");
            await _db.SaveChangesAsync();
        }

        return Ok(new { shareToken = report.ShareToken });
    }

    [HttpGet("/api/reports/shared")]
    public async Task<IActionResult> GetSharedWithMe()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var reports = await _db.SharedReports
            .Where(sr => sr.UserId == userId)
            .Include(sr => sr.Report)
                .ThenInclude(r => r!.Workspace)
            .Include(sr => sr.Report)
                .ThenInclude(r => r!.Datasource)
            .Include(sr => sr.Report)
                .ThenInclude(r => r!.Agent)
            .OrderByDescending(sr => sr.SharedAt)
            .Select(sr => new
            {
                sr.Report!.Id,
                sr.Report.Guid,
                sr.Report.Name,
                sr.Report.Status,
                workspaceName = sr.Report.Workspace != null ? sr.Report.Workspace.Name : null,
                datasourceName = sr.Report.Datasource != null ? sr.Report.Datasource.Name : null,
                agentName = sr.Report.Agent != null ? sr.Report.Agent.Name : null,
                sr.SharedAt
            })
            .ToListAsync();
        return Ok(reports);
    }

    [HttpGet("/api/reports")]
    public async Task<IActionResult> GetByWorkspace([FromQuery] string workspaceGuid, [FromQuery] int? agentId = null, [FromQuery] string? agentGuid = null)
    {
        var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == workspaceGuid);
        if (ws == null) return NotFound();

        // Resolve agent scoping: if agentGuid is supplied, translate to id.
        int? resolvedAgentId = agentId;
        if (!resolvedAgentId.HasValue && !string.IsNullOrEmpty(agentGuid))
        {
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Guid == agentGuid || a.Id.ToString() == agentGuid);
            if (agent != null) resolvedAgentId = agent.Id;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (!await _permissions.CanViewReportsAsync(ws.Id, userId))
        {
            var appUser = await _db.Users.FindAsync(userId);
            if (appUser?.Role == "SuperAdmin")
            {
                return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });
            }
            if (appUser?.Role == "OrgAdmin")
            {
                if ((appUser.OrganizationId ?? 0) != ws.OrganizationId)
                    return StatusCode(403, new { error = "You do not have access to workspaces in other organizations." });
                // OrgAdmin within own org can proceed
            }
            else
            {
                return StatusCode(403, new { error = "You do not have access to this workspace." });
            }
        }

        var query = _db.Reports.Where(r => r.WorkspaceId == ws.Id);
        if (resolvedAgentId.HasValue)
        {
            // Agent-scoped: return only reports tied to this agent. Legacy reports without
            // an AgentId that were created against the agent's datasource are also included
            // so upsert lookups can still find them when migrating to per-agent scoping.
            var agentForFallback = await _db.Agents
                .Where(a => a.Id == resolvedAgentId.Value)
                .Select(a => new { a.DatasourceId })
                .FirstOrDefaultAsync();
            var agentDsId = agentForFallback?.DatasourceId;
            query = query.Where(r =>
                r.AgentId == resolvedAgentId.Value
                || (r.AgentId == null && agentDsId != null && r.DatasourceId == agentDsId));
        }

        var reports = await query
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
                r.AgentId,
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

        if (report.WorkspaceId > 0)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (!await _permissions.CanViewReportsAsync(report.WorkspaceId, userId))
            {
                // Check report-level shared access
                var hasSharedAccess = await _db.SharedReports
                    .AnyAsync(sr => sr.ReportId == report.Id && sr.UserId == userId);
                if (!hasSharedAccess)
                {
                    var appUser = await _db.Users.FindAsync(userId);
                    if (appUser?.Role == "SuperAdmin")
                    {
                        return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });
                    }
                    else if (appUser?.Role == "OrgAdmin")
                    {
                        var ws = await _db.Workspaces.FindAsync(report.WorkspaceId);
                        if ((appUser.OrganizationId ?? 0) != (ws?.OrganizationId ?? 0))
                            return StatusCode(403, new { error = "You do not have access to workspaces in other organizations." });
                    }
                    else
                    {
                        return StatusCode(403, new { error = "You do not have access to this report." });
                    }
                }
            }
        }

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
        if (req.CanvasJson != null)
        {
            // Phase 34b B18: capture prior canvas state as an auto-revision before overwriting.
            if (!string.IsNullOrEmpty(report.CanvasJson) && report.CanvasJson != req.CanvasJson)
            {
                _db.ReportRevisions.Add(new ReportRevision
                {
                    ReportId = report.Id,
                    Kind = "Auto",
                    Name = null,
                    CanvasJson = report.CanvasJson,
                    ReportName = report.Name,
                    CreatedBy = userId,
                    CreatedAt = DateTime.UtcNow
                });

                // Trim auto-revisions to the most recent 20 per report.
                const int MaxAutoRevisions = 20;
                var autoCount = await _db.ReportRevisions
                    .CountAsync(r => r.ReportId == report.Id && r.Kind == "Auto");
                if (autoCount >= MaxAutoRevisions)
                {
                    var stale = await _db.ReportRevisions
                        .Where(r => r.ReportId == report.Id && r.Kind == "Auto")
                        .OrderByDescending(r => r.CreatedAt)
                        .Skip(MaxAutoRevisions - 1)
                        .ToListAsync();
                    _db.ReportRevisions.RemoveRange(stale);
                }
            }
            report.CanvasJson = req.CanvasJson;
        }
        if (req.Status != null) report.Status = req.Status;
        report.UpdatedAt = DateTime.UtcNow;

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

        // Remove shared report records
        var sharedRecords = await _db.SharedReports.Where(sr => sr.ReportId == report.Id).ToListAsync();
        _db.SharedReports.RemoveRange(sharedRecords);

        _db.Reports.Remove(report);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ─── Phase 34b — Revisions & Named Snapshots ─────────────────────────────

    [HttpGet("/api/reports/{guid}/revisions")]
    public async Task<IActionResult> GetRevisions(string guid, [FromQuery] string kind = "Auto")
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanViewReportsAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You do not have access to this report." });

        var list = await _db.ReportRevisions
            .Where(r => r.ReportId == report.Id && r.Kind == kind)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.Kind, r.Name, r.ReportName, r.CreatedBy, r.CreatedAt })
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("/api/reports/{guid}/snapshots")]
    public async Task<IActionResult> CreateSnapshot(string guid, [FromBody] CreateSnapshotRequest req)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to create snapshots." });

        if (string.IsNullOrWhiteSpace(req?.Name))
            return BadRequest(new { error = "Snapshot name is required." });

        var snap = new ReportRevision
        {
            ReportId = report.Id,
            Kind = "Snapshot",
            Name = req.Name.Trim(),
            CanvasJson = req.CanvasJson ?? report.CanvasJson,
            ReportName = report.Name,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.ReportRevisions.Add(snap);
        await _db.SaveChangesAsync();

        return Ok(new { snap.Id, snap.Kind, snap.Name, snap.CreatedAt });
    }

    [HttpPost("/api/reports/{guid}/revisions/{revId}/restore")]
    public async Task<IActionResult> RestoreRevision(string guid, int revId)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to restore revisions." });

        var rev = await _db.ReportRevisions
            .FirstOrDefaultAsync(r => r.Id == revId && r.ReportId == report.Id);
        if (rev == null) return NotFound(new { error = "Revision not found." });

        // Capture current state as an auto-revision before restoring.
        if (!string.IsNullOrEmpty(report.CanvasJson))
        {
            _db.ReportRevisions.Add(new ReportRevision
            {
                ReportId = report.Id,
                Kind = "Auto",
                Name = "Before restore",
                CanvasJson = report.CanvasJson,
                ReportName = report.Name,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        report.CanvasJson = rev.CanvasJson;
        report.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { success = true, canvasJson = rev.CanvasJson });
    }

    [HttpDelete("/api/reports/{guid}/revisions/{revId}")]
    public async Task<IActionResult> DeleteRevision(string guid, int revId)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to delete revisions." });

        var rev = await _db.ReportRevisions
            .FirstOrDefaultAsync(r => r.Id == revId && r.ReportId == report.Id);
        if (rev == null) return NotFound();

        _db.ReportRevisions.Remove(rev);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}

public class CreateSnapshotRequest
{
    public string? Name { get; set; }
    public string? CanvasJson { get; set; }
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
