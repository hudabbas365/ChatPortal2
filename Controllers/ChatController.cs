using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace AIInsights.Controllers;

[Authorize]
public class ChatController : Controller
{
    private readonly AppDbContext _db;
    private readonly CohereService _cohereService;
    private readonly JwtService _jwtService;
    private readonly IQueryExecutionService _queryService;
    private readonly ITokenBudgetService _tokenBudget;
    private readonly IWorkspacePermissionService _permissions;

    public ChatController(AppDbContext db, CohereService cohereService, JwtService jwtService, IQueryExecutionService queryService, ITokenBudgetService tokenBudget, IWorkspacePermissionService permissions)
    {
        _db = db;
        _cohereService = cohereService;
        _jwtService = jwtService;
        _queryService = queryService;
        _tokenBudget = tokenBudget;
        _permissions = permissions;
    }

    [HttpGet("/chat")]
    public IActionResult Index() => View();

    [HttpGet("/chat/embed")]
    public IActionResult Embed() => View();

    private async Task<int> ResolveWorkspaceIdAsync(string? wsRef)
    {
        if (string.IsNullOrEmpty(wsRef)) return 0;
        if (int.TryParse(wsRef, out var intId)) return intId;
        var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == wsRef);
        return ws?.Id ?? 0;
    }

    [HttpPost("/api/chat/send")]
    public async Task SendMessage([FromBody] SendMessageRequest req)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var wsId = await ResolveWorkspaceIdAsync(req.WorkspaceId);

        // Workspace permission check
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";
        var callerUser = await _db.Users.FindAsync(userId);
        if (callerUser?.Role == "SuperAdmin")
        {
            await Response.WriteAsync("data: {\"text\":\"SuperAdmin does not have access to the AI Insights portal.\"}\n\n");
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
            return;
        }

        if (wsId > 0 && !await _permissions.CanViewAsync(wsId, userId))
        {
            if (callerUser?.Role != "OrgAdmin")
            {
                await Response.WriteAsync("data: {\"text\":\"Access denied — you do not have access to this workspace.\"}\n\n");
                await Response.WriteAsync("data: [DONE]\n\n");
                await Response.Body.FlushAsync();
                return;
            }
        }

        // Check token budget for the organization
        int? orgId = null;
        if (!string.IsNullOrEmpty(userId))
        {
            orgId = await _db.Users.Where(u => u.Id == userId).Select(u => u.OrganizationId).FirstOrDefaultAsync();
        }

        if (orgId.HasValue && orgId.Value > 0)
        {
            if (!await _tokenBudget.HasBudgetAsync(orgId.Value))
            {
                var errData = $"data: {Newtonsoft.Json.JsonConvert.SerializeObject(new { text = "Monthly AI token budget exceeded. Contact your organisation admin." })}\n\n";
                await Response.WriteAsync(errData);
                await Response.WriteAsync("data: [DONE]\n\n");
                await Response.Body.FlushAsync();
                return;
            }
        }

        var history = new List<(string role, string content)>();
        if (wsId > 0)
        {
            IQueryable<ChatMessage> query = _db.ChatMessages.Where(m => m.WorkspaceId == wsId);
            if (!string.IsNullOrEmpty(req.AgentId))
                query = query.Where(m => m.AgentId == req.AgentId);
            var msgs = await query
                .OrderByDescending(m => m.CreatedAt)
                .Take(10)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
            history = msgs.Select(m => (m.Role, m.Content)).ToList();
        }

        // Look up agent → datasource for schema context
        string schemaContext = "";
        string datasourceIdentity = "";
        bool connectedToPowerBi = false;
        bool isRestApi = false;
        if (!string.IsNullOrEmpty(req.AgentId))
        {
            Agent? agent = null;
            if (int.TryParse(req.AgentId, out var agentIntId))
                agent = await _db.Agents.Include(a => a.Datasource).FirstOrDefaultAsync(a => a.Id == agentIntId);
            agent ??= await _db.Agents.Include(a => a.Datasource).FirstOrDefaultAsync(a => a.Guid == req.AgentId);

            if (agent?.Datasource != null)
            {
                schemaContext = await BuildSchemaPromptAsync(agent.Datasource);
                connectedToPowerBi = IsPowerBi(agent.Datasource.Type);
                isRestApi = IsRestApi(agent.Datasource.Type);
                datasourceIdentity = $"\n\n## Active Datasource\nYou are currently connected to **{agent.Datasource.Name}** (Type: {agent.Datasource.Type}). " +
                    $"All queries you generate MUST target this specific datasource. " +
                    $"Use the exact table and column names from the schema provided below. " +
                    $"Do NOT assume or invent table/column names that are not in the schema.";
            }
        }

        // Load report context when request comes from report viewer
        string reportContext = "";
        if (!string.IsNullOrEmpty(req.ReportGuid))
        {
            var report = await _db.Reports
                .Include(r => r.Datasource)
                .Include(r => r.Agent!).ThenInclude(a => a!.Datasource)
                .FirstOrDefaultAsync(r => r.Guid == req.ReportGuid);

            if (report != null)
            {
                reportContext = BuildReportContextPrompt(report);

                // Use report's datasource/agent for schema if no agent was explicitly specified
                if (string.IsNullOrEmpty(schemaContext))
                {
                    var ds = report.Agent?.Datasource ?? report.Datasource;
                    if (ds != null)
                    {
                        schemaContext = await BuildSchemaPromptAsync(ds);
                        connectedToPowerBi = IsPowerBi(ds.Type);
                        isRestApi = IsRestApi(ds.Type);
                        datasourceIdentity = $"\n\n## Active Datasource\nYou are currently connected to **{ds.Name}** (Type: {ds.Type}). " +
                            $"All queries you generate MUST target this specific datasource. " +
                            $"Use the exact table and column names from the schema provided below. " +
                            $"Do NOT assume or invent table/column names that are not in the schema.";
                    }
                }
            }
        }

        // Fallback: if no schema context yet, try the explicit datasourceId from the request
        if (string.IsNullOrEmpty(schemaContext) && req.DatasourceId.HasValue && req.DatasourceId.Value > 0)
        {
            var ds = await _db.Datasources.FindAsync(req.DatasourceId.Value);
            if (ds != null)
            {
                schemaContext = await BuildSchemaPromptAsync(ds);
                connectedToPowerBi = IsPowerBi(ds.Type);
                isRestApi = IsRestApi(ds.Type);
                datasourceIdentity = $"\n\n## Active Datasource\nYou are currently connected to **{ds.Name}** (Type: {ds.Type}). " +
                    $"All queries you generate MUST target this specific datasource. " +
                    $"Use the exact table and column names from the schema provided below. " +
                    $"Do NOT assume or invent table/column names that are not in the schema.";
            }
        }

        // Last-resort fallback: look up the workspace's first datasource
        if (string.IsNullOrEmpty(schemaContext) && wsId > 0)
        {
            var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.WorkspaceId == wsId);
            if (ds != null)
            {
                schemaContext = await BuildSchemaPromptAsync(ds);
                connectedToPowerBi = IsPowerBi(ds.Type);
                isRestApi = IsRestApi(ds.Type);
                datasourceIdentity = $"\n\n## Active Datasource\nYou are currently connected to **{ds.Name}** (Type: {ds.Type}). " +
                    $"All queries you generate MUST target this specific datasource. " +
                    $"Use the exact table and column names from the schema provided below. " +
                    $"Do NOT assume or invent table/column names that are not in the schema.";
            }
        }

        // Inject workspace memories into system prompt
        var defaultPrompt = isRestApi
            ? @"You are AI Insight's AI data assistant connected to a **REST API** datasource. The data has already been fetched from the API. When a user asks a data question:

1. **Understand Intent**: Determine what data the user wants.
2. **Analyze Data**: The API data is already available. Analyze the fields and sample data provided in the schema below.
3. **Provide Insights**: Give clear, actionable insights based on the data.
4. **Return Structure**: Always respond in this JSON format when a data query is involved:

{
  ""type"": ""data_response"",
  ""prompt"": ""The original user question rephrased as a clear intent"",
  ""query"": ""REST_API"",
  ""description"": ""Description of the data analysis performed."",
  ""suggestedChart"": ""bar"",
  ""suggestedFields"": { ""label"": ""fieldName"", ""value"": ""fieldName"" }
}

IMPORTANT: For REST API datasources, always set query to ""REST_API"" — the system will fetch the data automatically.
Use the field names from the schema provided. Suggest charts and fields based on the available data.
For non-data questions, respond normally in plain text."
            : connectedToPowerBi
            ? @"You are AI Insight's AI data assistant connected to a **Power BI semantic model**. When a user asks a data question:

1. **Understand Intent**: Determine what data the user wants.
2. **Generate Query**: Generate a **DAX** query for the Power BI semantic model. Do NOT use SQL.
3. **Provide Description**: Explain what the query does in plain English.
4. **Return Structure**: Always respond in this JSON format when a data query is involved:

{
  ""type"": ""data_response"",
  ""prompt"": ""The original user question rephrased as a clear intent"",
  ""query"": ""EVALUATE TOPN(10, SUMMARIZECOLUMNS('Sales'[Region], \""TotalRevenue\"", SUM('Sales'[Amount])))"",
  ""description"": ""This DAX query retrieves total revenue grouped by region from the semantic model."",
  ""suggestedChart"": ""bar"",
  ""suggestedFields"": { ""label"": ""Region"", ""value"": ""TotalRevenue"" }
}

IMPORTANT: Always generate DAX — never SQL. Use EVALUATE, SUMMARIZECOLUMNS, TOPN, FILTER, CALCULATETABLE, ADDCOLUMNS, VALUES.
Use single-quoted table names: 'TableName'. Use bracketed column names: [ColumnName].
For non-data questions, respond normally in plain text.
When the user asks to visualize or chart data, suggest appropriate chart types.
Always be concise and actionable."
            : @"You are AI Insight's AI data assistant. When a user asks a data question:

1. **Understand Intent**: Determine what data the user wants.
2. **Generate Query**: Based on the connected datasource, generate the appropriate query (SQL for relational databases, DAX for Power BI).
3. **Provide Description**: Explain what the query does in plain English.
4. **Return Structure**: Always respond in this JSON format when a data query is involved:

{
  ""type"": ""data_response"",
  ""prompt"": ""The original user question rephrased as a clear intent"",
  ""query"": ""SELECT region, SUM(revenue) as total_revenue FROM sales GROUP BY region ORDER BY total_revenue DESC"",
  ""description"": ""This query retrieves total revenue grouped by region, sorted from highest to lowest."",
  ""suggestedChart"": ""bar"",
  ""suggestedFields"": { ""label"": ""region"", ""value"": ""total_revenue"" }
}

For non-data questions, respond normally in plain text.
When the user asks to visualize or chart data, suggest appropriate chart types.
Always be concise and actionable.
IMPORTANT: Return ONLY a single query — never multiple statements. Do NOT include SQL comments (-- or /* */).";

        var effectiveSystemPrompt = req.SystemPrompt ?? defaultPrompt;
        if (!string.IsNullOrEmpty(reportContext))
            effectiveSystemPrompt += "\n\n" + reportContext;
        if (!string.IsNullOrEmpty(datasourceIdentity))
            effectiveSystemPrompt += datasourceIdentity;
        // When the user is viewing a specific report, keep schema brief to focus on report context
        if (!string.IsNullOrEmpty(schemaContext))
        {
            if (req.Context == "report_viewer" && !string.IsNullOrEmpty(reportContext))
            {
                effectiveSystemPrompt += "\n\n## Schema Reference (abbreviated)\n" +
                    "The full datasource schema is available but keep your focus on the report's existing charts and queries shown above. " +
                    "Only reference the schema when the user explicitly asks to explore data beyond the report.\n" +
                    schemaContext;
                effectiveSystemPrompt += "\n\n**IMPORTANT**: The user is currently viewing a specific report. " +
                    "Prioritize answering questions about the charts, data, and insights visible in THIS report. " +
                    "Describe what the charts show, explain trends, and summarize findings from the report context above. " +
                    "Only generate new queries when the user explicitly asks for additional data exploration.";
            }
            else
            {
                effectiveSystemPrompt += "\n\n" + schemaContext;
            }
        }
     

        var fullResponse = new StringBuilder();
        await foreach (var chunk in _cohereService.StreamChatAsync(
            req.Message ?? "",
            history,
            effectiveSystemPrompt))
        {
            fullResponse.Append(chunk);
            var data = $"data: {Newtonsoft.Json.JsonConvert.SerializeObject(new { text = chunk })}\n\n";
            await Response.WriteAsync(data);
            await Response.Body.FlushAsync();
        }

        await Response.WriteAsync("data: [DONE]\n\n");
        await Response.Body.FlushAsync();

        // Record token usage (estimate: 1 token ≈ 4 characters for input + output)
        if (orgId.HasValue && orgId.Value > 0)
        {
            var inputText = (req.Message ?? "") + effectiveSystemPrompt;
            var estimatedTokens = (inputText.Length + fullResponse.Length) / 4;
            if (estimatedTokens > 0)
                await _tokenBudget.RecordUsageAsync(orgId.Value, userId, estimatedTokens);
        }

        // Save messages to DB
        if (wsId > 0 && !string.IsNullOrEmpty(userId))
        {
            _db.ChatMessages.Add(new ChatMessage
            {
                Role = "user",
                Content = req.Message ?? "",
                WorkspaceId = wsId,
                AgentId = req.AgentId,
                UserId = userId
            });
            _db.ChatMessages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = fullResponse.ToString(),
                WorkspaceId = wsId,
                AgentId = req.AgentId,
                UserId = userId
            });
            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "agent_execution",
                Description = $"Chat message sent in workspace {wsId}.",
                UserId = userId
            });
            await _db.SaveChangesAsync();
        }
    }

    [HttpPost("/api/data/execute")]
    public async Task<IActionResult> ExecuteQuery([FromBody] ExecuteQueryRequest req)
    {
        var execUser2 = await _db.Users.FindAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
        if (execUser2?.Role == "SuperAdmin")
            return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });

        var query = (req.Query ?? "").Trim();

        // Strip SQL comments (AI often returns -- comments) and take only the first statement
        query = QueryExecutionService.StripSqlComments(query);
        if (!string.IsNullOrEmpty(query) && !query.TrimStart().StartsWith("EVALUATE", StringComparison.OrdinalIgnoreCase))
        {
            // Split on semicolons and take the first non-empty statement
            var firstStatement = query.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(firstStatement))
                query = firstStatement;
        }

        // Server-side read-only guard — block all write operations
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            query.ToUpperInvariant(), @"\s+", " ").Trim();
        var writeOps = new[] { "INSERT","UPDATE","DELETE","DROP","CREATE","ALTER",
                               "TRUNCATE","EXEC","EXECUTE","MERGE","CALL","GRANT",
                               "REVOKE","REPLACE","UPSERT","ATTACH","DETACH" };
        var firstToken = System.Text.RegularExpressions.Regex.Split(
            normalized.TrimStart(), @"[\s(;]+").FirstOrDefault() ?? "";

        // Allow DAX EVALUATE and DMV queries (read-only Power BI operations)
        // Allow REST_API marker (REST API datasources bypass SQL entirely)
        var isDaxOrDmv = firstToken == "EVALUATE"
            || firstToken == "REST_API"
            || (firstToken == "SELECT" && normalized.Contains("$SYSTEM."));

        if (!isDaxOrDmv &&
            (writeOps.Contains(firstToken) ||
            writeOps.Any(kw => System.Text.RegularExpressions.Regex.IsMatch(
                normalized, $@"\b{kw}\b"))))
        {
            return BadRequest(new
            {
                success = false,
                error = $"Write operation \"{firstToken}\" is not permitted. " +
                        "Only read-only queries (SELECT, EVALUATE) are allowed on this connection."
            });
        }

        // Look up the connected datasource and execute against it
        if (req.DatasourceId.HasValue && req.DatasourceId.Value > 0)
        {
            var ds = await _db.Datasources.FindAsync(req.DatasourceId.Value);

            if (ds != null)
            {
                var execUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
                var execUser = await _db.Users.FindAsync(execUserId);
                var callerOrgId = execUser?.OrganizationId ?? 0;

                // Org sandbox: non-SuperAdmins cannot run queries against datasources from another org
                if (execUser?.Role != "SuperAdmin" && callerOrgId > 0 && ds.OrganizationId != callerOrgId)
                    return StatusCode(403, new { success = false, error = "You do not have access to this datasource." });

                // Workspace-level permission check (OrgAdmins are scoped to own org above, so no bypass needed)
                if (ds.WorkspaceId.HasValue && ds.WorkspaceId.Value > 0)
                {
                    if (!string.IsNullOrEmpty(execUserId) && !await _permissions.CanViewAsync(ds.WorkspaceId.Value, execUserId))
                    {
                        if (execUser?.Role != "OrgAdmin" && execUser?.Role != "SuperAdmin")
                            return StatusCode(403, new { success = false, error = "You do not have access to this datasource." });
                    }
                }
            }

            if (ds != null)
            {
                // Power BI uses XmlaEndpoint instead of ConnectionString for connectivity
                var isPbi = QueryExecutionService.PowerBiTypes.Contains(ds.Type ?? "");
                var hasConnection = isPbi
                    ? !string.IsNullOrWhiteSpace(ds.XmlaEndpoint)
                    : !string.IsNullOrWhiteSpace(ds.ConnectionString);

                if (hasConnection || IsRestApi(ds.Type))
                    {
                        // REST API datasources: fetch data from the API instead of executing SQL
                        if (IsRestApi(ds.Type))
                        {
                            var apiResult = await _queryService.ExecuteRestApiAsync(ds);
                            return Ok(new { success = apiResult.Success, data = apiResult.Data, rowCount = apiResult.RowCount, error = apiResult.Error });
                        }

                        var result = await _queryService.ExecuteReadOnlyAsync(ds, query);
                        return Ok(new { success = result.Success, data = result.Data, rowCount = result.RowCount, error = result.Error });
                    }
            }
        }

        // Fallback: no datasource connected — return informative error
        return Ok(new
        {
            success = false,
            data = Array.Empty<object>(),
            rowCount = 0,
            error = "No datasource connected. Please connect a datasource with a valid connection string in the workspace wizard."
        });
    }

    [HttpPost("/api/chat/pin")]
    public async Task<IActionResult> PinResult([FromBody] PinRequest req)
    {
        var wsId = await ResolveWorkspaceIdAsync(req.WorkspaceId);

        if (wsId > 0)
        {
            var pinUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (!await _permissions.CanViewAsync(wsId, pinUserId))
            {
                var appUser = await _db.Users.FindAsync(pinUserId);
                if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                    return StatusCode(403, new { error = "You do not have access to this workspace." });
            }
        }

        var pinned = new PinnedResult
        {
            DatasetName = req.DatasetName ?? "pinned",
            JsonData = req.JsonData ?? "[]",
            ChatMessageId = req.ChatMessageId,
            WorkspaceId = wsId,
            UserId = req.UserId ?? ""
        };
        _db.PinnedResults.Add(pinned);
        await _db.SaveChangesAsync();
        return Ok(new { id = pinned.Id });
    }

    [HttpGet("/api/chat/history/{workspaceId}")]
    public async Task<IActionResult> GetHistory(int workspaceId)
    {
        var histUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (!await _permissions.CanViewAsync(workspaceId, histUserId))
        {
            var appUser = await _db.Users.FindAsync(histUserId);
            if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                return StatusCode(403, new { error = "You do not have access to this workspace." });
        }

        var msgs = await _db.ChatMessages
            .Where(m => m.WorkspaceId == workspaceId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        return Ok(msgs);
    }

    [HttpPost("/api/chat/analyze-image")]
    public async Task AnalyzeImage([FromBody] AnalyzeImageRequest req)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        // Workspace permission check
        var wsId = await ResolveWorkspaceIdAsync(req.WorkspaceId);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";
        var analyzeUser = await _db.Users.FindAsync(userId);
        if (analyzeUser?.Role == "SuperAdmin")
        {
            await Response.WriteAsync("data: {\"text\":\"SuperAdmin does not have access to the AI Insights portal.\"}\n\n");
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
            return;
        }

        if (wsId > 0 && !await _permissions.CanViewAsync(wsId, userId))
        {
            if (analyzeUser?.Role != "OrgAdmin")
            {
                await Response.WriteAsync("data: {\"text\":\"Access denied — you do not have access to this workspace.\"}\n\n");
                await Response.WriteAsync("data: [DONE]\n\n");
                await Response.Body.FlushAsync();
                return;
            }
        }

        // Check token budget for the organization
        int? orgId = null;
        if (!string.IsNullOrEmpty(userId))
        {
            orgId = await _db.Users.Where(u => u.Id == userId).Select(u => u.OrganizationId).FirstOrDefaultAsync();
        }
        if (orgId.HasValue && orgId.Value > 0)
        {
            if (!await _tokenBudget.HasBudgetAsync(orgId.Value))
            {
                var errData = $"data: {Newtonsoft.Json.JsonConvert.SerializeObject(new { text = "Monthly AI token budget exceeded. Contact your organisation admin." })}\n\n";
                await Response.WriteAsync(errData);
                await Response.WriteAsync("data: [DONE]\n\n");
                await Response.Body.FlushAsync();
                return;
            }
        }

        var fullResponse = new StringBuilder();
        await foreach (var chunk in _cohereService.AnalyzeImageStreamAsync(
            req.ImageDataUrl ?? "",
            req.Prompt ?? ""))
        {
            fullResponse.Append(chunk);
            var data = $"data: {Newtonsoft.Json.JsonConvert.SerializeObject(new { text = chunk })}\n\n";
            await Response.WriteAsync(data);
            await Response.Body.FlushAsync();
        }

        await Response.WriteAsync("data: [DONE]\n\n");
        await Response.Body.FlushAsync();

        // Persist to chat history
        if (!string.IsNullOrEmpty(req.UserId))
        {
            if (wsId > 0)
            {
                _db.ChatMessages.Add(new ChatMessage
                {
                    Role = "user",
                    Content = $"[Chart Analysis] {req.Prompt ?? "Analyze this chart"}",
                    WorkspaceId = wsId,
                    UserId = req.UserId
                });
                _db.ChatMessages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = fullResponse.ToString(),
                    WorkspaceId = wsId,
                    UserId = req.UserId
                });
                await _db.SaveChangesAsync();
            }
        }
    }

    private static bool IsPowerBi(string? type) => QueryExecutionService.PowerBiTypes.Contains(type ?? "");
    private static bool IsRestApi(string? type) => QueryExecutionService.RestApiTypes.Contains(type ?? "");

    private async Task<string> BuildSchemaPromptAsync(Datasource ds)
    {
        var isPbi = IsPowerBi(ds.Type);
        var isRest = IsRestApi(ds.Type);
        var sb = new StringBuilder();
        sb.AppendLine($"## Connected Datasource: {ds.Name} ({ds.Type})");

        // REST API: build schema from a sample API call
        if (isRest)
        {
            sb.AppendLine("You are connected to a REST API datasource. Data is fetched automatically — do NOT generate SQL.");
            sb.AppendLine("When the user asks a data question, set the query field to \"REST_API\" and the system will fetch the data.");
            sb.AppendLine("### API Data Fields:");
            try
            {
                var apiResult = await _queryService.ExecuteRestApiAsync(ds);
                if (apiResult.Success && apiResult.Data.Count > 0)
                {
                    var fields = apiResult.Data.First().Keys.ToList();
                    sb.AppendLine($"- **{ds.Name.Replace(" ", "_")}** (API Endpoint): {string.Join(", ", fields)}");
                    sb.AppendLine($"- Sample row count: {apiResult.Data.Count}");
                    // Show up to 3 sample rows for context
                    var sampleRows = apiResult.Data.Take(3);
                    sb.AppendLine("### Sample Data:");
                    foreach (var row in sampleRows)
                    {
                        var vals = row.Select(kv => $"{kv.Key}={kv.Value}");
                        sb.AppendLine($"  - {string.Join(", ", vals)}");
                    }
                }
                else
                {
                    sb.AppendLine("- Could not retrieve sample data from the API.");
                }
            }
            catch
            {
                sb.AppendLine("- Could not retrieve sample data from the API.");
            }
            sb.AppendLine();
            sb.AppendLine("Use these field names when suggesting charts. Always set query to \"REST_API\".");
            return sb.ToString();
        }

        if (isPbi)
        {
            sb.AppendLine("You are connected to a Power BI semantic model via XMLA. Generate **DAX** queries — NOT SQL.");
            sb.AppendLine("### DAX Query Rules:");
            sb.AppendLine("- Always start with `EVALUATE`.");
            sb.AppendLine("- Use single-quoted table names: `'Sales'`.");
            sb.AppendLine("- Use square-bracketed column names: `[Amount]`.");
            sb.AppendLine("- Use `SUMMARIZECOLUMNS`, `FILTER`, `CALCULATETABLE`, `TOPN`, `ADDCOLUMNS`, `VALUES` etc.");
            sb.AppendLine("- Do NOT use SQL keywords (SELECT, FROM, WHERE, GROUP BY, JOIN).");
            sb.AppendLine("- Example: `EVALUATE SUMMARIZECOLUMNS('Sales'[Region], \"TotalRevenue\", SUM('Sales'[Amount]))`");
        }
        else
        {
            sb.AppendLine("You are connected to this datasource. Generate SQL queries compatible with this database type.");
            if ((ds.Type ?? "").Contains("SQL Server", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("### SQL Rules:");
                sb.AppendLine("- ALWAYS use fully schema-qualified table names exactly as listed below (e.g., [SalesLT].[Product], [dbo].[BuildVersion]).");
                sb.AppendLine("- NEVER omit the schema prefix. Every table reference in FROM, JOIN, and subqueries MUST include the schema.");
                sb.AppendLine("- ALWAYS wrap every column name in square brackets (e.g., [Database Version], [ModifiedDate]). This is required because column names may contain spaces or reserved words.");
            }
        }

        sb.AppendLine("### Database Schema:");

        // Try real schema introspection
        var hasConn = isPbi
            ? !string.IsNullOrWhiteSpace(ds.XmlaEndpoint)
            : !string.IsNullOrWhiteSpace(ds.ConnectionString);

        if (hasConn)
        {
            try
            {
                var introspectionQuery = GetSchemaIntrospectionQuery(ds.Type);
                if (introspectionQuery != null)
                {
                    var result = await _queryService.ExecuteReadOnlyAsync(ds, introspectionQuery);
                    if (result.Success && result.Data.Count > 0)
                    {
                        var grouped = result.Data
                            .GroupBy(r => r.ContainsKey("table_name") ? r["table_name"]?.ToString() ?? "" : "")
                            .Where(g => !string.IsNullOrEmpty(g.Key));

                        foreach (var table in grouped)
                        {
                            var cols = table.Select(r =>
                            {
                                var col = r.ContainsKey("column_name") ? r["column_name"]?.ToString() : "";
                                var dt = r.ContainsKey("data_type") ? r["data_type"]?.ToString() : "";
                                return $"{col} {dt}";
                            });
                            sb.AppendLine($"- {table.Key} ({string.Join(", ", cols)})");
                        }

                        sb.AppendLine();
                        sb.AppendLine("Always use exact table and column names from the schema above — including the schema prefix (e.g., dbo.TableName or SalesLT.TableName).");
                        sb.AppendLine(isPbi ? "Generate DAX queries (not SQL) for this Power BI model." : $"Generate SQL appropriate for {ds.Type}.");
                        return sb.ToString();
                    }
                }
            }
            catch { /* fall through to placeholder schema */ }
        }

        // Fallback: placeholder schema based on selected tables
        var tableNames = string.IsNullOrEmpty(ds.SelectedTables)
            ? new[] { "Customers", "Orders", "Products", "Sales", "Employees" }
            : ds.SelectedTables.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var schemas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Customers"] = "Customers (Id INT PK, Name NVARCHAR, Email NVARCHAR, Region NVARCHAR, CreatedAt DATETIME)",
            ["Orders"] = "Orders (Id INT PK, CustomerId INT FK→Customers, OrderDate DATETIME, TotalAmount DECIMAL, Status NVARCHAR, Region NVARCHAR)",
            ["Products"] = "Products (Id INT PK, Name NVARCHAR, Category NVARCHAR, Price DECIMAL, StockQuantity INT)",
            ["Sales"] = "Sales (Id INT PK, ProductId INT FK→Products, CustomerId INT FK→Customers, Quantity INT, TotalRevenue DECIMAL, SaleDate DATETIME, Region NVARCHAR)",
            ["Employees"] = "Employees (Id INT PK, Name NVARCHAR, Department NVARCHAR, HireDate DATETIME, Salary DECIMAL)",
            ["vw_MonthlyRevenue"] = "vw_MonthlyRevenue (Month NVARCHAR, TotalRevenue DECIMAL, OrderCount INT)",
            ["vw_CustomerSummary"] = "vw_CustomerSummary (CustomerId INT, CustomerName NVARCHAR, TotalOrders INT, TotalSpent DECIMAL)",
            ["vw_TopProducts"] = "vw_TopProducts (ProductId INT, ProductName NVARCHAR, TotalSold INT, Revenue DECIMAL)"
        };

        foreach (var table in tableNames)
        {
            var trimmed = table.Trim();
            if (schemas.TryGetValue(trimmed, out var desc))
                sb.AppendLine($"- {desc}");
            else
                sb.AppendLine($"- {trimmed} (Id INT PK, Name NVARCHAR, Value DECIMAL, CreatedAt DATETIME)");
        }

        sb.AppendLine();
        sb.AppendLine("Always use exact table and column names from the schema above.");
        sb.AppendLine(isPbi ? "Generate DAX queries (not SQL) for this Power BI model." : $"Generate SQL appropriate for {ds.Type}.");
        return sb.ToString();
    }

    private static string BuildReportContextPrompt(Report report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Report Context: {report.Name}");
        sb.AppendLine($"You are answering questions about a specific report named \"{report.Name}\".");
        sb.AppendLine("The user is viewing this report right now. Use the chart information below to answer their questions.");

        if (!string.IsNullOrEmpty(report.CanvasJson))
        {
            try
            {
                var canvas = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(report.CanvasJson);
                var pages = canvas?["pages"] as Newtonsoft.Json.Linq.JArray;
                if (pages != null && pages.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("### Report Structure");
                    sb.AppendLine($"This report contains {pages.Count} page(s) with the following charts:");

                    foreach (var page in pages)
                    {
                        var pageName = page["name"]?.ToString() ?? "Untitled Page";
                        var charts = page["charts"] as Newtonsoft.Json.Linq.JArray;
                        if (charts == null || charts.Count == 0) continue;

                        sb.AppendLine($"\n**Page: {pageName}** ({charts.Count} chart(s))");
                        foreach (var chart in charts)
                        {
                            var title = chart["title"]?.ToString() ?? "Untitled Chart";
                            var chartType = chart["chartType"]?.ToString() ?? "unknown";
                            var dataQuery = chart["dataQuery"]?.ToString() ?? chart["DataQuery"]?.ToString();
                            var datasetName = chart["datasetName"]?.ToString() ?? chart["DatasetName"]?.ToString();
                            var filterWhere = chart["filterWhere"]?.ToString() ?? chart["FilterWhere"]?.ToString();

                            sb.AppendLine($"- **{title}** (Type: {chartType})");
                            if (!string.IsNullOrWhiteSpace(dataQuery))
                                sb.AppendLine($"  Query: `{dataQuery}`");
                            if (!string.IsNullOrWhiteSpace(datasetName))
                                sb.AppendLine($"  Dataset: {datasetName}");
                            if (!string.IsNullOrWhiteSpace(filterWhere))
                                sb.AppendLine($"  Filter: {filterWhere}");
                        }
                    }
                }
            }
            catch { /* JSON parse error — skip chart details */ }
        }

        sb.AppendLine();
        sb.AppendLine("When the user asks to summarize or explain this report, describe the charts, their purpose, and what data they display based on the queries and chart types above.");
        sb.AppendLine("When the user asks about trends or values, use the chart queries as reference to generate new queries or explain the data.");
        return sb.ToString();
    }

    [HttpGet("/api/chat/suggestions")]
    public async Task<IActionResult> GetSuggestions([FromQuery] string? agentId)
    {
        var suggestions = new List<object>();
        if (string.IsNullOrEmpty(agentId))
            return Ok(suggestions);

        Agent? agent = null;
        if (int.TryParse(agentId, out var agentIntId))
            agent = await _db.Agents.Include(a => a.Datasource).FirstOrDefaultAsync(a => a.Id == agentIntId);
        agent ??= await _db.Agents.Include(a => a.Datasource).FirstOrDefaultAsync(a => a.Guid == agentId);

        if (agent?.Datasource == null)
            return Ok(suggestions);

        var ds = agent.Datasource;
        var tables = new List<string>();

        // REST API: use field names from a sample API call for suggestions
        if (IsRestApi(ds.Type))
        {
            var apiDsName = ds.Name ?? "API";
            var fields = new List<string>();
            try
            {
                var apiResult = await _queryService.ExecuteRestApiAsync(ds);
                if (apiResult.Success && apiResult.Data.Count > 0)
                    fields = apiResult.Data.First().Keys.ToList();
            }
            catch { /* use generic suggestions */ }

            if (fields.Count > 0)
            {
                var firstField = fields.FirstOrDefault(f => !f.Equals("id", StringComparison.OrdinalIgnoreCase)) ?? fields[0];
                var numeric = fields.FirstOrDefault(f =>
                    f.Contains("amount", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("price", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("total", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("count", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("value", StringComparison.OrdinalIgnoreCase)) ?? fields.Last();
                suggestions.Add(new { text = $"Show me all data from {apiDsName}", icon = "bi-table" });
                suggestions.Add(new { text = $"What are the unique values of {firstField}?", icon = "bi-list-ul" });
                suggestions.Add(new { text = $"Summarize {numeric} from {apiDsName}", icon = "bi-graph-up" });
                suggestions.Add(new { text = $"Show the top 10 records", icon = "bi-sort-down" });
                suggestions.Add(new { text = $"What fields are available in {apiDsName}?", icon = "bi-diagram-3" });
            }
            else
            {
                suggestions.Add(new { text = $"Show me all data from {apiDsName}", icon = "bi-table" });
                suggestions.Add(new { text = $"What fields are available?", icon = "bi-diagram-3" });
                suggestions.Add(new { text = $"Summarize the data", icon = "bi-graph-up" });
            }
            return Ok(suggestions);
        }

        // Try real schema introspection for table names
        var hasDsConn = IsPowerBi(ds.Type)
            ? !string.IsNullOrWhiteSpace(ds.XmlaEndpoint)
            : !string.IsNullOrWhiteSpace(ds.ConnectionString);
        if (hasDsConn)
        {
            try
            {
                var sql = GetSchemaIntrospectionQuery(ds.Type);
                if (sql != null)
                {
                    var result = await _queryService.ExecuteReadOnlyAsync(ds, sql);
                    if (result.Success && result.Data.Count > 0)
                    {
                        tables = result.Data
                            .Select(r => r.ContainsKey("table_name") ? r["table_name"]?.ToString() ?? "" : "")
                            .Where(t => !string.IsNullOrEmpty(t))
                            .Distinct()
                            .Take(10)
                            .ToList();
                    }
                }
            }
            catch { /* use fallback */ }
        }

        // Fallback to selected tables
        if (!tables.Any() && !string.IsNullOrEmpty(ds.SelectedTables))
            tables = ds.SelectedTables.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();

        if (!tables.Any())
            tables = new List<string> { "data" };

        // Generate contextual suggestions based on table names
        var dsName = ds.Name ?? "database";
        var first = tables.FirstOrDefault() ?? "data";
        var second = tables.Count > 1 ? tables[1] : first;

        suggestions.Add(new { text = $"Show me all records from {first}", icon = "bi-table" });
        suggestions.Add(new { text = $"What are the top 10 rows in {first}?", icon = "bi-sort-down" });
        if (tables.Count > 1)
            suggestions.Add(new { text = $"How many records are in {second}?", icon = "bi-123" });
        suggestions.Add(new { text = $"Summarize the schema of {dsName}", icon = "bi-diagram-3" });
        suggestions.Add(new { text = $"Show trends or totals from {first}", icon = "bi-graph-up" });
        if (tables.Count > 2)
            suggestions.Add(new { text = $"Compare data between {first} and {tables[2]}", icon = "bi-arrow-left-right" });

        return Ok(suggestions);
    }

    private static string? GetSchemaIntrospectionQuery(string type)
    {
        var t = type?.Trim() ?? "";
        if (t.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) || t.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            return "SELECT t.TABLE_SCHEMA + '.' + t.TABLE_NAME as table_name, c.COLUMN_NAME as column_name, c.DATA_TYPE as data_type FROM INFORMATION_SCHEMA.TABLES t JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION";
        if (t.Contains("Postgre", StringComparison.OrdinalIgnoreCase))
            return "SELECT t.table_name, c.column_name, c.data_type FROM information_schema.tables t JOIN information_schema.columns c ON c.table_name = t.table_name AND c.table_schema = t.table_schema WHERE t.table_schema = 'public' ORDER BY t.table_name, c.ordinal_position";
        if (t.Contains("MySQL", StringComparison.OrdinalIgnoreCase) || t.Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
            return "SELECT t.TABLE_NAME as table_name, c.COLUMN_NAME as column_name, c.DATA_TYPE as data_type FROM INFORMATION_SCHEMA.TABLES t JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_NAME = t.TABLE_NAME AND c.TABLE_SCHEMA = t.TABLE_SCHEMA WHERE t.TABLE_SCHEMA = DATABASE() ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";
        // Power BI — DAX schema discovery via INFO functions
        if (t.Contains("Power BI", StringComparison.OrdinalIgnoreCase) || t.Equals("PowerBI", StringComparison.OrdinalIgnoreCase))
            return "EVALUATE VAR _tables = SELECTCOLUMNS(FILTER(INFO.TABLES(), NOT [IsHidden]), \"TableID\", [ID], \"table_name\", [Name]) " +
                   "VAR _cols = SELECTCOLUMNS(FILTER(INFO.COLUMNS(), NOT [IsHidden]), \"TableID\", [TableID], \"column_name\", [ExplicitName], " +
                   "\"data_type\", SWITCH([DataType], 2, \"String\", 6, \"Int64\", 8, \"Double\", 9, \"DateTime\", 10, \"Decimal\", 11, \"Boolean\", \"Other\")) " +
                   "RETURN SELECTCOLUMNS(NATURALLEFTOUTERJOIN(_tables, _cols), \"table_name\", [table_name], \"column_name\", [column_name], \"data_type\", [data_type])";
        return null;
    }
}

public class SendMessageRequest
{
    public string? Message { get; set; }
    public string? SystemPrompt { get; set; }
    public string? WorkspaceId { get; set; }
    public string? AgentId { get; set; }
    public int? DatasourceId { get; set; }
    public string? UserId { get; set; }
    public string? ReportGuid { get; set; }
    public string? Context { get; set; }
    public int? PageIndex { get; set; }
}

public class PinRequest
{
    public string? DatasetName { get; set; }
    public string? JsonData { get; set; }
    public int ChatMessageId { get; set; }
    public string? WorkspaceId { get; set; }
    public string? UserId { get; set; }
}

public class ExecuteQueryRequest
{
    public string? Query { get; set; }
    public int? DatasourceId { get; set; }
    public string? UserId { get; set; }
}

public class AnalyzeImageRequest
{
    public string? ImageDataUrl { get; set; }
    public string? Prompt { get; set; }
    public string? WorkspaceId { get; set; }
    public string? UserId { get; set; }
}

