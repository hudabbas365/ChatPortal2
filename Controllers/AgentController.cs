using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChatPortal2.Controllers;

[Authorize]
[Route("api/agents")]
[ApiController]
public class AgentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWorkspacePermissionService _permissions;
    private readonly UserManager<ApplicationUser> _userManager;

    public AgentController(AppDbContext db, IWorkspacePermissionService permissions, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _permissions = permissions;
        _userManager = userManager;
    }

    private async Task<int> ResolveOrganizationIdAsync(int supplied, string? userId)
    {
        if (supplied > 0) return supplied;
        if (!string.IsNullOrEmpty(userId))
        {
            var userOrgId = await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.OrganizationId)
                .FirstOrDefaultAsync();
            if (userOrgId.HasValue && userOrgId.Value > 0) return userOrgId.Value;
        }
        var org = new Organization { Name = "Default Organization" };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(userId))
        {
            var userEntity = await _db.Users.FindAsync(userId);
            if (userEntity != null)
            {
                userEntity.OrganizationId = org.Id;
                await _db.SaveChangesAsync();
            }
        }

        return org.Id;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? workspaceId, [FromQuery] int? organizationId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);

        // Every request is scoped to the caller's own organization — ignore any organizationId param
        // for non-SuperAdmins to prevent cross-org data leakage.
        var callerOrgId = appUser?.OrganizationId ?? 0;
        if (appUser?.Role != "SuperAdmin" && callerOrgId <= 0)
            return StatusCode(403, new { error = "User is not assigned to an organization." });

        // If workspace context provided, block Viewers from seeing agents
        if (workspaceId.HasValue && workspaceId.Value > 0)
        {
            if (!await _permissions.CanViewAgentsAsync(workspaceId.Value, userId))
                return StatusCode(403, new { error = "AI Insights are not available for Viewers." });
        }

        var query = _db.Agents.AsQueryable();

        if (appUser?.Role == "SuperAdmin")
        {
            // SuperAdmins may optionally filter by org
            if (organizationId.HasValue && organizationId.Value > 0)
                query = query.Where(a => a.OrganizationId == organizationId.Value);
        }
        else
        {
            // All other roles are hard-scoped to their own org only
            query = query.Where(a => a.OrganizationId == callerOrgId);
        }

        // Non-OrgAdmin users only see agents from workspaces they own or are a member of
        var isOrgLevel = appUser?.Role == "OrgAdmin" || appUser?.Role == "SuperAdmin";
        if (!isOrgLevel)
        {
            query = query.Where(a =>
                !a.WorkspaceId.HasValue ||
                _db.Workspaces.Any(w => w.Id == a.WorkspaceId && w.OwnerId == userId) ||
                _db.WorkspaceUsers.Any(wu => wu.WorkspaceId == a.WorkspaceId && wu.UserId == userId));
        }

        var agents = await query.ToListAsync();
        return Ok(agents);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AgentRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        if (req.WorkspaceId.HasValue && req.WorkspaceId.Value > 0)
        {
            if (!await _permissions.CanEditAsync(req.WorkspaceId.Value, userId))
                return StatusCode(403, new { error = "You need Editor or Admin role to create agents." });
        }

        var orgId = await ResolveOrganizationIdAsync(req.OrganizationId, userId);

        // Enforce org sandbox: non-SuperAdmins can only create agents in their own org
        if (appUser?.Role != "SuperAdmin" && callerOrgId > 0 && orgId != callerOrgId)
            return StatusCode(403, new { error = "You cannot create agents in another organization." });

        var agent = new Agent
        {
            Name = req.Name ?? "New Agent",
            SystemPrompt = req.SystemPrompt ?? "You are a helpful data assistant.",
            DatasourceId = req.DatasourceId,
            WorkspaceId = req.WorkspaceId,
            OrganizationId = orgId
        };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "agent_created",
            Description = $"Agent '{agent.Name}' created.",
            UserId = userId,
            OrganizationId = orgId
        });
        await _db.SaveChangesAsync();

        return Ok(new { agent.Id, agent.Guid, agent.Name, agent.SystemPrompt, agent.DatasourceId, agent.WorkspaceId, agent.OrganizationId });
    }

    [HttpPut("{guid}")]
    public async Task<IActionResult> Update(string guid, [FromBody] AgentRequest req)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Guid == guid);
        if (agent == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        // Org sandbox: non-SuperAdmins cannot modify agents from a different organization
        if (appUser?.Role != "SuperAdmin" && callerOrgId > 0 && agent.OrganizationId != callerOrgId)
            return StatusCode(403, new { error = "You do not have access to this agent." });

        var wsId = agent.WorkspaceId ?? req.WorkspaceId ?? 0;
        if (wsId > 0 && !await _permissions.CanEditAsync(wsId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to update agents." });

        if (req.Name != null) agent.Name = req.Name;
        if (req.SystemPrompt != null) agent.SystemPrompt = req.SystemPrompt;
        if (req.DatasourceId.HasValue) agent.DatasourceId = req.DatasourceId;

        await _db.SaveChangesAsync();
        return Ok(new { agent.Id, agent.Guid, agent.Name, agent.DatasourceId, agent.WorkspaceId });
    }

    [HttpDelete("{guid}")]
    public async Task<IActionResult> Delete(string guid)
    {
        Agent? agent = null;
        if (int.TryParse(guid, out var intId))
            agent = await _db.Agents.FindAsync(intId);
        if (agent == null)
            agent = await _db.Agents.FirstOrDefaultAsync(a => a.Guid == guid);
        if (agent == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        // Org sandbox: non-SuperAdmins cannot delete agents from a different organization
        if (appUser?.Role != "SuperAdmin" && callerOrgId > 0 && agent.OrganizationId != callerOrgId)
            return StatusCode(403, new { error = "You do not have access to this agent." });

        var wsId = agent.WorkspaceId ?? 0;
        if (wsId > 0 && !await _permissions.CanDeleteAsync(wsId, userId))
            return StatusCode(403, new { error = "Only Admins can delete agents." });

        // Null out Dashboard references to avoid FK constraint failures
        var dashboards = await _db.Dashboards.Where(d => d.AgentId == agent.Id).ToListAsync();
        foreach (var d in dashboards) d.AgentId = null;

        _db.Agents.Remove(agent);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("generate-prompt")]
    public IActionResult GeneratePrompt([FromBody] GeneratePromptRequest req)
    {
        var agentName = req.AgentName ?? "Data Assistant";
        var workspaceName = req.WorkspaceName ?? "the workspace";
        var dsName = req.DatasourceName;
        var dsType = req.DatasourceType;
        var tables = req.SelectedTables;

        var sb = new System.Text.StringBuilder();
        sb.Append($"You are {agentName}, an AI data assistant for the \"{workspaceName}\" workspace. ");

        if (!string.IsNullOrEmpty(dsName))
            sb.Append($"You are connected to the \"{dsName}\" datasource ({dsType ?? "database"}). ");

        if (!string.IsNullOrEmpty(tables))
            sb.Append($"Available tables and views: {tables}. ");

        sb.AppendLine("Your responsibilities include:");
        sb.AppendLine("1. Answering data-related questions by generating accurate SQL queries");
        sb.AppendLine("2. Analyzing query results and providing clear, actionable insights");
        sb.AppendLine("3. Suggesting appropriate chart types and visualizations for the data");
        sb.AppendLine("4. Explaining data patterns, trends, and anomalies");
        sb.AppendLine("5. Helping users explore and understand their data effectively");
        sb.AppendLine();
        sb.Append("Always validate your SQL syntax, explain your reasoning, and format results clearly. ");
        sb.Append("When suggesting visualizations, specify the recommended chart type and which fields to use.");

        return Ok(new { prompt = sb.ToString() });
    }
}

public class AgentRequest
{
    public string? Name { get; set; }
    public string? SystemPrompt { get; set; }
    public int? DatasourceId { get; set; }
    public int? WorkspaceId { get; set; }
    public int OrganizationId { get; set; }
    public string? UserId { get; set; }
}

public class GeneratePromptRequest
{
    public string? AgentName { get; set; }
    public string? WorkspaceName { get; set; }
    public string? DatasourceName { get; set; }
    public string? DatasourceType { get; set; }
    public string? SelectedTables { get; set; }
}
