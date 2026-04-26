using AIInsights.Data;
using AIInsights.Filters;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.Controllers;

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

        if (appUser?.Role == "SuperAdmin")
            return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });

        // Every request is scoped to the caller's own organization — ignore any organizationId param
        // for non-SuperAdmins to prevent cross-org data leakage.
        var callerOrgId = appUser?.OrganizationId ?? 0;
        if (callerOrgId <= 0)
            return StatusCode(403, new { error = "User is not assigned to an organization." });

        // If workspace context provided, block Viewers from seeing agents
        if (workspaceId.HasValue && workspaceId.Value > 0)
        {
            if (!await _permissions.CanViewAgentsAsync(workspaceId.Value, userId))
                return StatusCode(403, new { error = "AI Insights are not available for Viewers." });
        }

        var query = _db.Agents.AsQueryable();

        // All roles are hard-scoped to their own org only
        query = query.Where(a => a.OrganizationId == callerOrgId);

        // Non-OrgAdmin users only see agents from workspaces they own or are a member of
        var isOrgLevel = appUser?.Role == "OrgAdmin";
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
    [RequireActiveSubscription]
    public async Task<IActionResult> Create([FromBody] AgentRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        if (appUser?.Role == "SuperAdmin")
            return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });

        if (req.WorkspaceId.HasValue && req.WorkspaceId.Value > 0)
        {
            if (!await _permissions.CanEditAsync(req.WorkspaceId.Value, userId))
                return StatusCode(403, new { error = "You need Editor or Admin role to create agents." });
        }

        var orgId = await ResolveOrganizationIdAsync(req.OrganizationId, userId);

        // Enforce org sandbox: non-SuperAdmins can only create agents in their own org
        if (callerOrgId > 0 && orgId != callerOrgId)
            return StatusCode(403, new { error = "You cannot create agents in another organization." });

        // Validate that workspace belongs to caller's org
        if (req.WorkspaceId.HasValue && req.WorkspaceId.Value > 0)
        {
            if (!await _permissions.BelongsToSameOrganizationAsync(req.WorkspaceId.Value, orgId))
                return StatusCode(403, new { error = "Workspace does not belong to your organization." });
        }

        // SECURITY: never trust a client-supplied SystemPrompt. Rebuild it server-side
        // from the bound datasource + workspace + its SelectedTables so a tampered
        // wizard (or a direct API call) cannot inject hostile instructions into the
        // model's system prompt. The agent name is sanitised before being interpolated.
        var safeName = SanitiseShortText(req.Name, fallback: "New Agent", maxLen: 80);
        var systemPrompt = await BuildSystemPromptForAgentAsync(safeName, req.DatasourceId, req.WorkspaceId);

        var agent = new Agent
        {
            Name = safeName,
            SystemPrompt = systemPrompt,
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
    [RequireActiveSubscription]
    public async Task<IActionResult> Update(string guid, [FromBody] AgentRequest req)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Guid == guid);
        if (agent == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        if (appUser?.Role == "SuperAdmin")
            return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });

        // Org sandbox: non-SuperAdmins cannot modify agents from a different organization
        if (callerOrgId > 0 && agent.OrganizationId != callerOrgId)
            return StatusCode(403, new { error = "You do not have access to this agent." });

        var wsId = agent.WorkspaceId ?? req.WorkspaceId;
        if (wsId == null || wsId == 0)
            return StatusCode(403, new { error = "Agent must be associated with a workspace." });
        if (!await _permissions.CanEditAsync(wsId.Value, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to update agents." });

        if (req.Name != null) agent.Name = SanitiseShortText(req.Name, fallback: agent.Name ?? "Agent", maxLen: 80);
        if (req.DatasourceId.HasValue) agent.DatasourceId = req.DatasourceId;

        // SECURITY: ignore any client-supplied SystemPrompt. Always rebuild it server-side
        // from the (possibly newly-bound) datasource + selected tables so the prompt cannot
        // be tampered with via DevTools or a hand-crafted PUT.
        agent.SystemPrompt = await BuildSystemPromptForAgentAsync(agent.Name ?? "Agent", agent.DatasourceId, agent.WorkspaceId);

        await _db.SaveChangesAsync();
        return Ok(new { agent.Id, agent.Guid, agent.Name, agent.DatasourceId, agent.WorkspaceId });
    }

    [HttpDelete("{guid}")]
    [RequireActiveSubscription]
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

        if (appUser?.Role == "SuperAdmin")
            return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });

        // Org sandbox: non-SuperAdmins cannot delete agents from a different organization
        if (callerOrgId > 0 && agent.OrganizationId != callerOrgId)
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
    public async Task<IActionResult> GeneratePrompt([FromBody] GeneratePromptRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);

        if (appUser?.Role == "SuperAdmin")
            return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });

        // Org validation — workspace must belong to caller's org
        if (req.WorkspaceId.HasValue && req.WorkspaceId.Value > 0)
        {
            var ws = await _db.Workspaces.FindAsync(req.WorkspaceId.Value);
            if (ws == null || ws.OrganizationId != (appUser?.OrganizationId ?? 0))
                return StatusCode(403, new { error = "Workspace does not belong to your organization." });
        }

        var prompt = BuildSystemPrompt(
            SanitiseShortText(req.AgentName, fallback: "Data Assistant", maxLen: 80),
            SanitiseShortText(req.WorkspaceName, fallback: "the workspace", maxLen: 120),
            SanitiseShortText(req.DatasourceName, fallback: "", maxLen: 120),
            SanitiseShortText(req.DatasourceType, fallback: "", maxLen: 60),
            SanitiseShortText(req.SelectedTables, fallback: "", maxLen: 4000));

        return Ok(new { prompt });
    }

    /// <summary>
    /// Loads the bound datasource + workspace and rebuilds the canonical system prompt.
    /// This is the only path that should ever set Agent.SystemPrompt — client-supplied
    /// prompts are ignored on Create/Update for security (prompt-injection defence).
    /// </summary>
    private async Task<string> BuildSystemPromptForAgentAsync(string agentName, int? datasourceId, int? workspaceId)
    {
        string workspaceName = "the workspace";
        string datasourceName = "";
        string datasourceType = "";
        string selectedTables = "";

        if (workspaceId.HasValue && workspaceId.Value > 0)
        {
            var ws = await _db.Workspaces.FindAsync(workspaceId.Value);
            if (ws != null) workspaceName = ws.Name ?? workspaceName;
        }
        if (datasourceId.HasValue && datasourceId.Value > 0)
        {
            var ds = await _db.Datasources.FindAsync(datasourceId.Value);
            if (ds != null)
            {
                datasourceName = ds.Name ?? "";
                datasourceType = ds.Type ?? "";
                selectedTables = ds.SelectedTables ?? "";
            }
        }

        return BuildSystemPrompt(
            SanitiseShortText(agentName, fallback: "Data Assistant", maxLen: 80),
            SanitiseShortText(workspaceName, fallback: "the workspace", maxLen: 120),
            SanitiseShortText(datasourceName, fallback: "", maxLen: 120),
            SanitiseShortText(datasourceType, fallback: "", maxLen: 60),
            SanitiseShortText(selectedTables, fallback: "", maxLen: 4000));
    }

    /// <summary>
    /// Strips control characters and newlines, trims, and caps length. Used to make
    /// caller-supplied free-text safe to interpolate into the system prompt template
    /// without enabling prompt-injection via the agent / workspace / datasource name.
    /// </summary>
    private static string SanitiseShortText(string? value, string fallback, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        // Drop control chars (incl. CR/LF) so a user can't inject "\n\nIGNORE PRIOR INSTRUCTIONS" via a name.
        var cleaned = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '\r' || ch == '\n' || ch == '\t') { cleaned.Append(' '); continue; }
            if (char.IsControl(ch)) continue;
            cleaned.Append(ch);
        }
        var s = System.Text.RegularExpressions.Regex.Replace(cleaned.ToString(), @"\s+", " ").Trim();
        if (string.IsNullOrEmpty(s)) return fallback;
        if (s.Length > maxLen) s = s.Substring(0, maxLen);
        return s;
    }

    private static string BuildSystemPrompt(string agentName, string workspaceName, string datasourceName, string datasourceType, string selectedTables)
    {
        var dsType = datasourceType ?? "";
        bool isPowerBi = Services.QueryExecutionService.PowerBiTypes.Contains(dsType);
        bool isRestApi = Services.QueryExecutionService.RestApiTypes.Contains(dsType);
        bool isFileUrl = Services.QueryExecutionService.FileUrlTypes.Contains(dsType);
        string queryLang = isRestApi ? "REST API" : isFileUrl ? "File URL" : isPowerBi ? "DAX" : "SQL";

        var sb = new System.Text.StringBuilder();
        sb.Append($"You are {agentName}, an AI data assistant for the \"{workspaceName}\" workspace. ");

        if (!string.IsNullOrEmpty(datasourceName))
            sb.Append($"You are connected to the \"{datasourceName}\" datasource ({dsType}). ");

        if (!string.IsNullOrEmpty(selectedTables))
        {
            var tableLabel = isRestApi ? "Available API fields" : isFileUrl ? "Available file columns" : isPowerBi ? "Available tables/measures" : "Available tables and views";
            sb.Append($"{tableLabel}: {selectedTables}. ");
        }

        sb.AppendLine("Your responsibilities include:");
        if (isRestApi || isFileUrl)
        {
            sb.AppendLine("1. Answering data-related questions by analyzing the pre-fetched data");
            sb.AppendLine("2. Providing clear, actionable insights based on the data fields available");
        }
        else
        {
            sb.AppendLine($"1. Answering data-related questions by generating accurate {queryLang} queries");
            sb.AppendLine("2. Analyzing query results and providing clear, actionable insights");
        }
        sb.AppendLine("3. Suggesting appropriate chart types and visualizations for the data");
        sb.AppendLine("4. Explaining data patterns, trends, and anomalies");
        sb.AppendLine("5. Helping users explore and understand their data effectively");
        sb.AppendLine();

        if (isRestApi)
        {
            sb.AppendLine("IMPORTANT: This is a REST API datasource. Do NOT generate SQL or DAX queries.");
            sb.AppendLine("Always set the query field to \"REST_API\" — the system fetches data automatically from the API.");
            sb.AppendLine("Suggest charts and field mappings based on the available API fields.");
        }
        else if (isFileUrl)
        {
            sb.AppendLine("IMPORTANT: This is a File URL datasource (CSV/Excel). Do NOT generate SQL or DAX queries.");
            sb.AppendLine("Always set the query field to \"FILE_URL\" — the system fetches and parses the file automatically.");
            sb.AppendLine("Suggest charts and field mappings based on the available file columns.");
        }
        else if (isPowerBi)
        {
            sb.AppendLine("IMPORTANT: Always generate DAX queries (not SQL) for this Power BI semantic model.");
            sb.AppendLine("Use EVALUATE, SUMMARIZECOLUMNS, CALCULATETABLE, TOPN, ADDCOLUMNS, VALUES, FILTER.");
            sb.AppendLine("Use single-quoted table names: 'TableName'[ColumnName]. Never use SELECT/FROM/WHERE.");
        }
        else
        {
            sb.Append($"Always generate valid {dsType} SQL syntax, explain your reasoning, and format results clearly. ");
        }

        sb.Append("When suggesting visualizations, specify the recommended chart type and which fields to use.");
        return sb.ToString();
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
    public int? WorkspaceId { get; set; }
}
