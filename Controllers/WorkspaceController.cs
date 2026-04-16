using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.Controllers;

[Authorize]
public class WorkspaceController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWorkspacePermissionService _permissions;
    private readonly ILogger<WorkspaceController> _logger;

    public WorkspaceController(AppDbContext db, UserManager<ApplicationUser> userManager, IWorkspacePermissionService permissions, ILogger<WorkspaceController> logger)
    {
        _db = db;
        _userManager = userManager;
        _permissions = permissions;
        _logger = logger;
    }

    [HttpGet("/api/workspaces")]
    public async Task<IActionResult> GetAll([FromQuery] int organizationId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        // Org sandbox: non-SuperAdmins are always hard-scoped to their own org.
        // SuperAdmins may use the organizationId query param as a filter.
        if (appUser?.Role != "SuperAdmin")
        {
            if (callerOrgId <= 0)
                return StatusCode(403, new { error = "User is not assigned to an organization." });
            organizationId = callerOrgId;
        }

        var isOrgLevel = appUser?.Role == "OrgAdmin" || appUser?.Role == "SuperAdmin";

        var query = _db.Workspaces.Where(w => w.OrganizationId == organizationId);

        // Non-OrgAdmin users only see workspaces they own or are a member of
        if (!isOrgLevel)
        {
            query = query.Where(w =>
                w.OwnerId == userId ||
                _db.WorkspaceUsers.Any(wu => wu.WorkspaceId == w.Id && wu.UserId == userId));
        }

        var workspaces = await query.ToListAsync();
        return Ok(workspaces.Select(w => new
        {
            w.Id,
            w.Guid,
            w.Name,
            w.Description,
            w.OrganizationId,
            w.CreatedAt
        }));
    }

    [HttpGet("/api/workspaces/{guid}")]
    public async Task<IActionResult> GetByGuid(string guid)
    {
        var workspace = await _db.Workspaces
            .Include(w => w.Owner)
            .FirstOrDefaultAsync(w => w.Guid == guid);

        // Fallback: if guid looks like an integer, try finding by Id
        if (workspace == null && int.TryParse(guid, out var intId))
        {
            workspace = await _db.Workspaces
                .Include(w => w.Owner)
                .FirstOrDefaultAsync(w => w.Id == intId);
        }

        if (workspace == null) return NotFound();

        // Access check: user must be owner, explicit member, or OrgAdmin of the SAME org / SuperAdmin
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        if (appUser?.Role == "SuperAdmin")
        {
            // SuperAdmins are blocked from AI Insights portal
            return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });
        }

        // Prevent Workspace Admin of Org A from reading Workspace B
        if (callerOrgId > 0 && workspace.OrganizationId != callerOrgId)
            return StatusCode(403, new { error = "You do not have access to workspaces in other organizations." });

        if (appUser?.Role == "OrgAdmin")
        {
            // OrgAdmins can only access workspaces within their own org (already checked above)
        }
        else if (workspace.OwnerId != userId)
        {
            var hasAccess = await _db.WorkspaceUsers
                .AnyAsync(wu => wu.WorkspaceId == workspace.Id && wu.UserId == userId);
            if (!hasAccess)
                return StatusCode(403, new { error = "You do not have access to this workspace." });
        }

        var agents = await _db.Agents
            .Include(a => a.Datasource)
            .Where(a => a.WorkspaceId == workspace.Id)
            .Select(a => new
            {
                a.Id,
                a.Guid,
                a.Name,
                a.SystemPrompt,
                a.DatasourceId,
                datasourceName = a.Datasource != null ? a.Datasource.Name : null,
                datasourceGuid = a.Datasource != null ? a.Datasource.Guid : null,
                datasourceType = a.Datasource != null ? a.Datasource.Type : null
            })
            .ToListAsync();

        var datasources = await _db.Datasources
            .Where(d => d.WorkspaceId == workspace.Id)
            .Select(d => new { d.Id, d.Guid, d.Name, d.Type })
            .ToListAsync();

        var dashboards = await _db.Dashboards
            .Where(d => d.WorkspaceId == workspace.Id)
            .Select(d => new { d.Id, d.Guid, d.Name, d.AgentId, d.DatasourceId, d.CreatedAt })
            .ToListAsync();

        var reports = await _db.Reports
            .Where(r => r.WorkspaceId == workspace.Id)
            .Select(r => new { r.Id, r.Guid, r.Name, r.Status, r.DatasourceId, r.AgentId, r.CreatedAt })
            .ToListAsync();

        return Ok(new
        {
            workspace.Id,
            workspace.Guid,
            workspace.Name,
            workspace.Description,
            workspace.LogoUrl,
            workspace.OwnerId,
            ownerName = workspace.Owner?.FullName,
            ownerEmail = workspace.Owner?.Email,
            workspace.OrganizationId,
            workspace.CreatedAt,
            agents,
            datasources,
            dashboards,
            reports
        });
    }

    [HttpPost("/api/workspaces")]
    public async Task<IActionResult> Create([FromBody] WorkspaceRequest req)
    {
        var orgId = req.OrganizationId;

        // Resolve a valid OrganizationId if missing or zero
        if (orgId <= 0 && !string.IsNullOrEmpty(req.UserId))
        {
            var appUser = await _db.Users
                .Where(u => u.Id == req.UserId)
                .Select(u => u.OrganizationId)
                .FirstOrDefaultAsync();
            if (appUser.HasValue && appUser.Value > 0) orgId = appUser.Value;
        }

        // Still invalid — auto-create a default organization and assign to user
        if (orgId <= 0)
        {
            var org = new Organization { Name = "Default Organization" };
            _db.Organizations.Add(org);
            await _db.SaveChangesAsync();
            orgId = org.Id;

            if (!string.IsNullOrEmpty(req.UserId))
            {
                var userEntity = await _db.Users.FindAsync(req.UserId);
                if (userEntity != null)
                {
                    userEntity.OrganizationId = orgId;
                    await _db.SaveChangesAsync();
                }
            }
        }

        // Unique workspace name within the organization
        var trimmedName = (req.Name ?? "New Workspace").Trim();
        var nameExists = await _db.Workspaces.AnyAsync(w =>
            w.OrganizationId == orgId &&
            w.Name.ToLower() == trimmedName.ToLower());
        if (nameExists)
            return Conflict(new { error = "A workspace with this name already exists." });

        // Enforce workspace limit based on plan
        var callerOrg = await _db.Organizations.FindAsync(orgId);
        if (callerOrg != null && callerOrg.MaxWorkspaces > 0)
        {
            var wsCount = await _db.Workspaces.CountAsync(w => w.OrganizationId == orgId);
            if (wsCount >= callerOrg.MaxWorkspaces)
                return StatusCode(403, new { error = $"Your plan allows a maximum of {callerOrg.MaxWorkspaces} workspace(s) In current plan per Orgnization" });
        }

        var workspace = new Workspace
        {
            Name = trimmedName,
            Description = req.Description,
            LogoUrl = req.LogoUrl,
            OwnerId = req.UserId,
            OrganizationId = orgId
        };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        // Auto-create a default dashboard for the workspace
        var dashboard = new Dashboard
        {
            Name = "Dashboard",
            WorkspaceId = workspace.Id
        };
        _db.Dashboards.Add(dashboard);

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "workspace_created",
            Description = $"Workspace '{workspace.Name}' created.",
            UserId = req.UserId ?? "",
            OrganizationId = orgId
        });
        await _db.SaveChangesAsync();

        return Ok(new
        {
            workspace.Id,
            workspace.Guid,
            workspace.Name,
            workspace.Description,
            workspace.OrganizationId,
            workspace.CreatedAt,
            dashboardGuid = dashboard.Guid
        });
    }

    [HttpPut("/api/workspaces/{guid}")]
    public async Task<IActionResult> Update(string guid, [FromBody] WorkspaceRequest req)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == guid);
        if (workspace == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (!await _permissions.CanEditAsync(workspace.Id, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to update workspaces." });

        if (req.Name != null)
        {
            var trimmed = req.Name.Trim();
            var nameExists = await _db.Workspaces.AnyAsync(w =>
                w.OrganizationId == workspace.OrganizationId &&
                w.Id != workspace.Id &&
                w.Name.ToLower() == trimmed.ToLower());
            if (nameExists)
                return Conflict(new { error = "A workspace with this name already exists." });
            workspace.Name = trimmed;
        }
        if (req.Description != null) workspace.Description = req.Description;
        if (req.LogoUrl != null) workspace.LogoUrl = req.LogoUrl;
        if (req.OwnerId != null)
        {
            if (!await _permissions.CanDeleteAsync(workspace.Id, userId))
                return StatusCode(403, new { error = "Only Admins can transfer workspace ownership." });
            workspace.OwnerId = req.OwnerId;
        }

        await _db.SaveChangesAsync();

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "workspace_updated",
            Description = $"Workspace '{workspace.Name}' updated.",
            UserId = req.UserId ?? "",
            OrganizationId = workspace.OrganizationId
        });
        await _db.SaveChangesAsync();

        return Ok(new { workspace.Id, workspace.Guid, workspace.Name });
    }

    [HttpDelete("/api/workspaces/{guid}")]
    public async Task<IActionResult> Delete(string guid)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == guid);
        if (workspace == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (!await _permissions.CanDeleteAsync(workspace.Id, userId))
            return StatusCode(403, new { error = "Only Admins can delete workspaces." });
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // Explicitly remove child entities to avoid FK constraint failures
            var datasources = await _db.Datasources.Where(d => d.WorkspaceId == workspace.Id).ToListAsync();
            var agents = await _db.Agents.Where(a => a.WorkspaceId == workspace.Id).ToListAsync();
            var dashboards = await _db.Dashboards.Where(d => d.WorkspaceId == workspace.Id).ToListAsync();
            var wsUsers = await _db.WorkspaceUsers.Where(wu => wu.WorkspaceId == workspace.Id).ToListAsync();
            var reports = await _db.Reports.Where(r => r.WorkspaceId == workspace.Id).ToListAsync();

            // Null out agent references to datasources before removing
            foreach (var a in agents) a.DatasourceId = null;
            await _db.SaveChangesAsync();

            _db.WorkspaceUsers.RemoveRange(wsUsers);
            _db.Datasources.RemoveRange(datasources);
            _db.Agents.RemoveRange(agents);
            _db.Dashboards.RemoveRange(dashboards);
            _db.Reports.RemoveRange(reports);
            _db.Workspaces.Remove(workspace);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Failed to delete workspace {WorkspaceGuid}", guid);
            return StatusCode(500, new { error = "Failed to delete workspace." });
        }
    }

    [HttpDelete("/api/workspaces/{wsGuid}/insights/{dsGuid}")]
    public async Task<IActionResult> DeleteInsights(string wsGuid, string dsGuid)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == wsGuid);
        if (workspace == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var callerUser2 = await _db.Users.FindAsync(userId);
        if (callerUser2?.Role != "SuperAdmin")
        {
            var callerOrg2 = callerUser2?.OrganizationId ?? 0;
            if (callerOrg2 > 0 && workspace.OrganizationId != callerOrg2)
                return StatusCode(403, new { error = "You do not have access to workspaces in other organizations." });
        }

        if (!await _permissions.CanDeleteAsync(workspace.Id, userId))
            return StatusCode(403, new { error = "Only Admins can delete AI Insights." });

        // Find the datasource
        Datasource? ds = null;
        if (int.TryParse(dsGuid, out var intId))
            ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Id == intId && d.WorkspaceId == workspace.Id);
        if (ds == null)
            ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == dsGuid && d.WorkspaceId == workspace.Id);
        if (ds == null) return NotFound();

        // Find all agents bound to this datasource
        var agents = await _db.Agents
            .Where(a => a.DatasourceId == ds.Id && a.WorkspaceId == workspace.Id)
            .ToListAsync();
        var agentIds = agents.Select(a => a.Id).ToList();

        // Find all reports linked to this datasource or its agents
        var reports = await _db.Reports
            .Where(r => r.WorkspaceId == workspace.Id &&
                        (r.DatasourceId == ds.Id || (r.AgentId.HasValue && agentIds.Contains(r.AgentId.Value))))
            .ToListAsync();

        // Find all dashboards linked to this datasource or its agents
        var dashboards = await _db.Dashboards
            .Where(d => d.WorkspaceId == workspace.Id &&
                        (d.DatasourceId == ds.Id || (d.AgentId.HasValue && agentIds.Contains(d.AgentId.Value))))
            .ToListAsync();

        // Remove in correct order to avoid FK issues
        _db.Reports.RemoveRange(reports);
        _db.Dashboards.RemoveRange(dashboards);
        foreach (var a in agents) a.DatasourceId = null;
        await _db.SaveChangesAsync();

        _db.Agents.RemoveRange(agents);
        _db.Datasources.Remove(ds);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            deleted = new
            {
                datasources = 1,
                agents = agents.Count,
                reports = reports.Count,
                dashboards = dashboards.Count
            }
        });
    }

    // — Workspace User Management ——————————————————————————————————————

    [HttpGet("/api/workspaces/{guid}/users")]
    public async Task<IActionResult> GetUsers(string guid)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == guid);
        if (workspace == null && int.TryParse(guid, out var intId))
            workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == intId);
        if (workspace == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (!await _permissions.CanViewAsync(workspace.Id, userId))
        {
            var appUser = await _db.Users.FindAsync(userId);
            // OrgAdmins may only view members of workspaces in their own org
            if (appUser?.Role == "OrgAdmin")
            {
                if ((appUser.OrganizationId ?? 0) != workspace.OrganizationId)
                    return StatusCode(403, new { error = "You do not have access to this workspace." });
            }
            else if (appUser?.Role != "SuperAdmin")
            {
                return StatusCode(403, new { error = "You do not have access to this workspace." });
            }
        }

        var members = await _db.WorkspaceUsers
            .Where(wu => wu.WorkspaceId == workspace.Id)
            .Include(wu => wu.User)
            .Select(wu => new
            {
                wu.Id,
                wu.UserId,
                wu.Role,
                wu.CreatedAt,
                fullName = wu.User != null ? wu.User.FullName : "",
                email    = wu.User != null ? wu.User.Email   : ""
            })
            .ToListAsync();
        return Ok(members);
    }

    [HttpPost("/api/workspaces/{guid}/users")]
    public async Task<IActionResult> AddUser(string guid, [FromBody] AddWorkspaceUserRequest req)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == guid);
        if (workspace == null && int.TryParse(guid, out var intId))
            workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == intId);
        if (workspace == null) return NotFound(new { error = "Workspace not found." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var role = await _permissions.GetRoleAsync(workspace.Id, userId);
        if (role != "Admin")
            return StatusCode(403, new { error = "Only Admins can manage workspace members." });

        var targetUser = await _userManager.FindByEmailAsync(req.Email ?? "");
        if (targetUser == null)
            return BadRequest(new { error = $"No user found with email '{req.Email}'." });

        // Org sandbox: only users belonging to the same organization may be added to a workspace.
        // Return 404 (not 403) to avoid leaking the existence of users in other orgs.
        if (targetUser.OrganizationId.HasValue && targetUser.OrganizationId.Value != workspace.OrganizationId)
            return NotFound(new { error = $"No user found with email '{req.Email}'." });

        var already = await _db.WorkspaceUsers
            .AnyAsync(wu => wu.WorkspaceId == workspace.Id && wu.UserId == targetUser.Id);
        if (already)
            return BadRequest(new { error = "This user already has access to the workspace." });

        var allowedRoles = new[] { "Admin", "Editor", "Viewer" };
        var assignRole = req.Role ?? "Viewer";
        if (!allowedRoles.Contains(assignRole))
            return BadRequest(new { error = $"Invalid role '{assignRole}'. Allowed: Admin, Editor, Viewer." });

        var entry = new WorkspaceUser
        {
            WorkspaceId = workspace.Id,
            UserId      = targetUser.Id,
            Role        = assignRole
        };
        _db.WorkspaceUsers.Add(entry);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            entry.Id,
            entry.UserId,
            entry.Role,
            entry.CreatedAt,
            fullName = targetUser.FullName,
            email    = targetUser.Email
        });
    }

    [HttpDelete("/api/workspaces/{guid}/users/{userId}")]
    public async Task<IActionResult> RemoveUser(string guid, string userId)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == guid);
        if (workspace == null && int.TryParse(guid, out var intId))
            workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == intId);
        if (workspace == null) return NotFound();

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var currentRole = await _permissions.GetRoleAsync(workspace.Id, currentUserId);
        if (currentRole != "Admin")
            return StatusCode(403, new { error = "Only Admins can manage workspace members." });

        var entry = await _db.WorkspaceUsers
            .FirstOrDefaultAsync(wu => wu.WorkspaceId == workspace.Id && wu.UserId == userId);
        if (entry == null) return NotFound(new { error = "Member not found." });

        _db.WorkspaceUsers.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPut("/api/workspaces/{guid}/users/{userId}/role")]
    public async Task<IActionResult> UpdateUserRole(string guid, string userId, [FromBody] UpdateWsUserRoleRequest req)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == guid);
        if (workspace == null && int.TryParse(guid, out var intId2))
            workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == intId2);
        if (workspace == null) return NotFound();

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var currentRole = await _permissions.GetRoleAsync(workspace.Id, currentUserId);
        if (currentRole != "Admin")
            return StatusCode(403, new { error = "Only Admins can manage workspace members." });

        var entry = await _db.WorkspaceUsers
            .FirstOrDefaultAsync(wu => wu.WorkspaceId == workspace.Id && wu.UserId == userId);
        if (entry == null) return NotFound(new { error = "Member not found." });

        var allowedRoles = new[] { "Admin", "Editor", "Viewer" };
        var newRole = req.Role ?? entry.Role;
        if (!allowedRoles.Contains(newRole))
            return BadRequest(new { error = $"Invalid role '{newRole}'. Allowed: Admin, Editor, Viewer." });

        entry.Role = newRole;
        await _db.SaveChangesAsync();
        return Ok(new { entry.UserId, entry.Role });
    }

    [HttpGet("/api/workspaces/{guid}/myrole")]
    public async Task<IActionResult> GetMyRole(string guid)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == guid);
        if (workspace == null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var isOrgAdmin = user.Role == "OrgAdmin" || user.Role == "SuperAdmin";

        // Owner gets Admin
        if (workspace.OwnerId == user.Id)
            return Ok(new { role = "Admin", isOwner = true, isOrgAdmin });

        var wu = await _db.WorkspaceUsers
            .FirstOrDefaultAsync(w => w.WorkspaceId == workspace.Id && w.UserId == user.Id);

        // OrgAdmin/SuperAdmin can access any workspace in their org
        if (wu == null && isOrgAdmin)
            return Ok(new { role = "Admin", isOwner = false, isOrgAdmin });

        // No membership = no access
        if (wu == null)
            return StatusCode(403, new { error = "You do not have access to this workspace." });

        return Ok(new { role = wu.Role, isOwner = false, isOrgAdmin });
    }

    [HttpGet("/api/workspaces/{guid}/org-users")]
    public async Task<IActionResult> GetOrgUsersForPicker(string guid)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == guid);
        if (workspace == null) return NotFound();

        // Only OrgAdmin/SuperAdmin or workspace Admin can list org users for the picker
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var isOrgLevel = appUser?.Role == "OrgAdmin" || appUser?.Role == "SuperAdmin";
        if (!isOrgLevel)
        {
            var role = await _permissions.GetRoleAsync(workspace.Id, userId);
            if (role != "Admin")
                return StatusCode(403, new { error = "Only Admins can manage workspace members." });
        }

        var existingUserIds = await _db.WorkspaceUsers
            .Where(wu => wu.WorkspaceId == workspace.Id)
            .Select(wu => wu.UserId)
            .ToListAsync();

        var orgUsers = await _db.Users
            .Where(u => u.OrganizationId == workspace.OrganizationId
                     && !existingUserIds.Contains(u.Id)
                     && u.Id != workspace.OwnerId)
            .Select(u => new { u.Id, u.FullName, u.Email, u.Role })
            .ToListAsync();

        return Ok(orgUsers);
    }


}

public class WorkspaceRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? OwnerId { get; set; }
    public int OrganizationId { get; set; }
    public string? UserId { get; set; }
}

public class AddWorkspaceUserRequest
{
    public string? Email { get; set; }
    public string? Role { get; set; }
}

public class UpdateWsUserRoleRequest
{
    public string? Role { get; set; }
}

public class AddMemoryRequest
{
    public string? Content { get; set; }
    public string? Category { get; set; }
}
