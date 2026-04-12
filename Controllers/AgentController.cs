using ChatPortal2.Data;
using ChatPortal2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Controllers;

[Authorize]
[Route("api/agents")]
[ApiController]
public class AgentController : ControllerBase
{
    private readonly AppDbContext _db;

    public AgentController(AppDbContext db)
    {
        _db = db;
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
        var query = _db.Agents.AsQueryable();
        if (organizationId.HasValue)
            query = query.Where(a => a.OrganizationId == organizationId.Value);
        var agents = await query.ToListAsync();
        return Ok(agents);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AgentRequest req)
    {
        var orgId = await ResolveOrganizationIdAsync(req.OrganizationId, req.UserId);

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
            UserId = req.UserId ?? "",
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
