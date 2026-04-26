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
    private readonly IRelationshipService _relationships;
    private readonly IQueryCacheInvalidator _cacheInvalidator;

    public ChatController(AppDbContext db, CohereService cohereService, JwtService jwtService, IQueryExecutionService queryService, ITokenBudgetService tokenBudget, IWorkspacePermissionService permissions, IRelationshipService relationships, IQueryCacheInvalidator cacheInvalidator)
    {
        _db = db;
        _cohereService = cohereService;
        _jwtService = jwtService;
        _queryService = queryService;
        _tokenBudget = tokenBudget;
        _permissions = permissions;
        _relationships = relationships;
        _cacheInvalidator = cacheInvalidator;
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
        // ── Defense-in-depth: gate chart-explain calls on the server ──────────────
        // If the client tagged this request as "chart_explain", enforce
        // PlanFeatures.AllowsChartExplain before starting the SSE stream so that
        // Professional-plan users cannot bypass the client-side gate by calling
        // /api/chat/send directly.  Mirrors the AutoReportController.Generate gate.
        if (string.Equals(req.Source, "chart_explain", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(req.UserId))
        {
            var userSub = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == req.UserId);
            PlanType chartExplainPlan;
            if (userSub != null)
            {
                chartExplainPlan = userSub.Plan;
            }
            else
            {
                var gateUser = await _db.Users.FindAsync(req.UserId);
                var gateOrg = gateUser?.OrganizationId != null ? await _db.Organizations.FindAsync(gateUser.OrganizationId) : null;
                chartExplainPlan = gateOrg?.Plan ?? PlanType.Free;
            }
            if (!PlanFeatures.AllowsChartExplain(chartExplainPlan))
            {
                Response.StatusCode = 403;
                Response.ContentType = "application/json";
                await Response.WriteAsync("{\"error\":\"\\\"Explain by AI\\\" is not available on your current plan. Upgrade to Enterprise to unlock this feature.\",\"code\":\"plan_gated\"}");
                return;
            }
        }

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
        bool isFileUrl = false;
        Datasource? activeDatasource = null;
        if (!string.IsNullOrEmpty(req.AgentId))
        {
            Agent? agent = null;
            if (int.TryParse(req.AgentId, out var agentIntId))
                agent = await _db.Agents.Include(a => a.Datasource).FirstOrDefaultAsync(a => a.Id == agentIntId);
            agent ??= await _db.Agents.Include(a => a.Datasource).FirstOrDefaultAsync(a => a.Guid == req.AgentId);

            if (agent?.Datasource != null)
            {
                activeDatasource = agent.Datasource;
                schemaContext = await BuildSchemaPromptAsync(agent.Datasource);
                connectedToPowerBi = IsPowerBi(agent.Datasource.Type);
                isRestApi = IsRestApi(agent.Datasource.Type);
                isFileUrl = IsFileUrl(agent.Datasource.Type);
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
                        activeDatasource = ds;
                        schemaContext = await BuildSchemaPromptAsync(ds);
                        connectedToPowerBi = IsPowerBi(ds.Type);
                        isRestApi = IsRestApi(ds.Type);
                        isFileUrl = IsFileUrl(ds.Type);
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
                activeDatasource = ds;
                schemaContext = await BuildSchemaPromptAsync(ds);
                connectedToPowerBi = IsPowerBi(ds.Type);
                isRestApi = IsRestApi(ds.Type);
                isFileUrl = IsFileUrl(ds.Type);
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
                activeDatasource = ds;
                schemaContext = await BuildSchemaPromptAsync(ds);
                connectedToPowerBi = IsPowerBi(ds.Type);
                isRestApi = IsRestApi(ds.Type);
                isFileUrl = IsFileUrl(ds.Type);
                datasourceIdentity = $"\n\n## Active Datasource\nYou are currently connected to **{ds.Name}** (Type: {ds.Type}). " +
                    $"All queries you generate MUST target this specific datasource. " +
                    $"Use the exact table and column names from the schema provided below. " +
                    $"Do NOT assume or invent table/column names that are not in the schema.";
            }
        }

        // Inject workspace memories into system prompt
        var defaultPrompt = isRestApi
            ? @"You are AI Insight's friendly data assistant. You speak to business end-users, not developers. The data already comes from a connected source — the user doesn't need to know it's an API.

When the user asks a data question, respond in this JSON format:

{
  ""type"": ""data_response"",
  ""prompt"": ""The original user question rephrased as a clear intent"",
  ""query"": ""REST_API"",
  ""description"": ""Here's a quick look at your top customers so you can see who drives the most revenue."",
  ""suggestedChart"": ""bar"",
  ""suggestedFields"": { ""label"": ""fieldName"", ""value"": ""fieldName"" },
  ""followUpSuggestions"": [
    ""Who are the top 5 customers?"",
    ""How has this changed over time?"",
    ""Break it down by region""
  ]
}

IMPORTANT RULES:
- Always set ""query"" to ""REST_API"" — the system fetches the data automatically.
- Return ONE unified answer per response — never multiple queries, never describe separate datasets.
- The ""description"" must sound like a helpful colleague summarizing the insight. NEVER mention queries, records, rows, columns, tables, schema, SQL, DAX, databases, API, joins, or that data was fetched.
- BAD example (do NOT write like this): ""This query retrieves all records from dbo.BuildVersion and SalesLT.Address separately. Since these tables are unrelated, they are queried independently.""
- GOOD example: ""Here's a quick look at your version info alongside customer addresses.""
- Always include ""followUpSuggestions"": exactly 3 short, business-friendly next questions phrased the way a real user would ask them.
- For non-data follow-ups (e.g. ""explain more"", ""why"", ""tell me more""): reply in plain friendly language for an end user. NEVER show code blocks, SQL, DAX, table names, dbo./schema prefixes, column types, or markdown headers like ""Query"" / ""Type"" / ""Description"". Describe the INSIGHT in 2–4 short sentences. Never expose internal details or error codes.
- EVERY response (data or plain text) MUST end with a single line containing 3 follow-up questions in this exact machine-readable tag: <followups>[""question 1"",""question 2"",""question 3""]</followups>. No other text after that tag."
            : isFileUrl
            ? @"You are AI Insight's friendly data assistant. You speak to business end-users, not developers. The data comes from a CSV or Excel file connected via a public share link.

When the user asks a data question, respond in this JSON format:

{
  ""type"": ""data_response"",
  ""prompt"": ""The original user question rephrased as a clear intent"",
  ""query"": ""FILE_URL"",
  ""description"": ""Here's a summary of the data from your file."",
  ""suggestedChart"": ""bar"",
  ""suggestedFields"": { ""label"": ""fieldName"", ""value"": ""fieldName"" },
  ""followUpSuggestions"": [
    ""Show me the top 10 rows"",
    ""What are the unique values of the first column?"",
    ""Summarize the numeric columns""
  ]
}

IMPORTANT RULES:
- Always set ""query"" to ""FILE_URL"" — the system fetches and parses the file automatically.
- Return ONE unified answer per response.
- The ""description"" must sound like a helpful colleague summarizing the insight. NEVER mention SQL, DAX, databases, API, or internal details.
- Always include ""followUpSuggestions"": exactly 3 short, business-friendly next questions.
- For non-data follow-ups: reply in plain friendly language. NEVER show code or technical details.
- EVERY response MUST end with: <followups>[""question 1"",""question 2"",""question 3""]</followups>. No other text after that tag."
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
  ""description"": ""Shows the top regions by total revenue so you can see where sales are strongest."",
  ""suggestedChart"": ""bar"",
  ""suggestedFields"": { ""label"": ""Region"", ""value"": ""TotalRevenue"" },
  ""followUpSuggestions"": [
    ""Which region grew the most this quarter?"",
    ""Break this down by product category"",
    ""Show the lowest performing regions""
  ]
}

IMPORTANT RULES:
- Always generate DAX — never SQL. Use EVALUATE, SUMMARIZECOLUMNS, TOPN, FILTER, CALCULATETABLE, ADDCOLUMNS, VALUES.
- Use single-quoted table names: 'TableName'. Use bracketed column names: [ColumnName].
- Return ONLY a single query — never multiple statements or multiple ""query"" fields. One unified query per response.
- The ""description"" must be written for a non-technical business user. Do NOT mention DAX, tables, columns, schema, or any technical terms. Describe the INSIGHT, not the mechanics.
- Always include ""followUpSuggestions"": an array of exactly 3 short, business-friendly next questions the user might naturally ask, phrased as the user would speak them (no jargon).
- BAD description example (do NOT write like this): ""This query retrieves all records from 'BuildVersion' and 'Address' separately. Since these tables are unrelated, they are queried independently.""
- GOOD description example: ""Here's a quick look at your version info alongside customer addresses.""
- For non-data follow-ups (e.g. ""explain more"", ""why this?"", ""tell me more""): reply in plain friendly language for an end user. NEVER show DAX/SQL code blocks, EVALUATE/SUMMARIZECOLUMNS snippets, table names, column names, or markdown headers like ""Query"" / ""Type"" / ""Description"". Describe the INSIGHT in 2–4 short sentences.
- EVERY response (data or plain text) MUST end with a single line containing 3 follow-up questions in this exact machine-readable tag: <followups>[""question 1"",""question 2"",""question 3""]</followups>. No other text after that tag.
- Never expose internal details, query syntax, table names, or error codes."
            : @"You are AI Insight's friendly data assistant. You speak to business end-users, not developers.

When the user asks a data question:

1. Understand what they want to see.
2. Build a single query for the connected datasource (SQL for relational, DAX for Power BI).
3. Explain the INSIGHT in plain business language — never mention SQL, tables, columns, joins, schema, or any technical terms.
4. Respond in this JSON format whenever a data query is involved:

{
  ""type"": ""data_response"",
  ""prompt"": ""The original user question rephrased as a clear intent"",
  ""query"": ""SELECT region, SUM(revenue) as total_revenue FROM sales GROUP BY region ORDER BY total_revenue DESC"",
  ""description"": ""See which regions bring in the most revenue so you can focus on top performers."",
  ""suggestedChart"": ""bar"",
  ""suggestedFields"": { ""label"": ""region"", ""value"": ""total_revenue"" },
  ""followUpSuggestions"": [
    ""How did the top region change over time?"",
    ""Compare revenue by product line"",
    ""Show me the regions that are underperforming""
  ]
}

IMPORTANT RULES:
- Return ONLY a single query — never multiple SQL statements, never multiple ""query"" fields, never UNION of unrelated queries. One unified query per response.
- Do NOT include SQL comments (-- or /* */).
- The ""description"" must sound like a helpful colleague summarizing the insight — no technical jargon, no query explanations, no mention of tables/columns/DAX/SQL.
- Always include ""followUpSuggestions"": an array of exactly 3 short, business-friendly next questions phrased the way a real user would ask them.
- BAD description example (do NOT write like this): ""This query retrieves all records from dbo.BuildVersion and SalesLT.Address separately. Since these tables are unrelated, they are queried independently.""
- GOOD description example: ""Here's a quick look at your version info alongside customer addresses.""
- For non-data follow-ups (e.g. ""explain more"", ""why"", ""tell me more""): reply in plain friendly language for an end user. NEVER show SQL/DAX code blocks (no ```sql or ```dax fences), no SELECT/FROM/WHERE snippets, no table names, no dbo./schema prefixes, no column types, no markdown headers like ""Query"" / ""Type"" / ""Description"". Describe the INSIGHT in 2–4 short sentences.
- EVERY response (data or plain text) MUST end with a single line containing 3 follow-up questions in this exact machine-readable tag: <followups>[""question 1"",""question 2"",""question 3""]</followups>. No other text after that tag.
- For non-data questions, reply in plain friendly language. Never expose internal details, query syntax, error codes, or stack traces.
- If something goes wrong or the request is unclear, apologize briefly and suggest what the user could try next — never show technical error text.";

        var effectiveSystemPrompt = req.SystemPrompt ?? defaultPrompt;
        if (!string.IsNullOrEmpty(reportContext))
            effectiveSystemPrompt += "\n\n" + reportContext;
        if (!string.IsNullOrEmpty(datasourceIdentity))
            effectiveSystemPrompt += datasourceIdentity;

        // Inject discovered foreign-key relationships so the AI emits correct JOINs.
        if (activeDatasource != null)
        {
            try
            {
                var rels = await _relationships.GetRelationshipsAsync(activeDatasource);
                if (rels != null && rels.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("\n\n## Known Table Relationships (use these for JOINs)");
                    sb.AppendLine("The left-hand column is a foreign key referencing the right-hand column. Prefer these when joining tables — do NOT invent joins that are not listed here.");
                    foreach (var r in rels.Take(40))
                        sb.AppendLine($"- {r.FromTable}.{r.FromColumn} -> {r.ToTable}.{r.ToColumn}");
                    effectiveSystemPrompt += sb.ToString();
                }
            }
            catch { /* best-effort */ }
        }
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
        // Allow REST_API / FILE_URL markers (these datasources bypass SQL entirely)
        var isDaxOrDmv = firstToken == "EVALUATE"
            || firstToken == "REST_API"
            || firstToken == "FILE_URL"
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
                // Honour an explicit refresh request from the client (NoCache body flag or
                // ?nocache=true query string) by flushing every cached result for this
                // datasource before executing, guaranteeing a live round-trip.
                var noCacheQuery = string.Equals(Request.Query["nocache"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
                if (req.NoCache || noCacheQuery)
                    _cacheInvalidator.InvalidateDatasource(ds.Id);

                // Power BI uses XmlaEndpoint instead of ConnectionString for connectivity
                var isPbi = QueryExecutionService.PowerBiTypes.Contains(ds.Type ?? "");
                var hasConnection = isPbi
                    ? !string.IsNullOrWhiteSpace(ds.XmlaEndpoint)
                    : !string.IsNullOrWhiteSpace(ds.ConnectionString);

                if (hasConnection || IsRestApi(ds.Type) || IsFileUrl(ds.Type))
                    {
                        // REST API datasources: fetch data from the API instead of executing SQL
                        if (IsRestApi(ds.Type))
                        {
                            var apiResult = await _queryService.ExecuteRestApiAsync(ds);
                            return Ok(new { success = apiResult.Success, data = apiResult.Data, rowCount = apiResult.RowCount, error = apiResult.Error });
                        }

                        // File URL (CSV/XLSX) datasources: fetch and parse the file instead of executing SQL
                        if (IsFileUrl(ds.Type))
                        {
                            var fileResult = await _queryService.ExecuteFileUrlAsync(ds);
                            return Ok(new { success = fileResult.Success, data = fileResult.Data, rowCount = fileResult.RowCount, error = fileResult.Error });
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
    private static bool IsFileUrl(string? type) => QueryExecutionService.FileUrlTypes.Contains(type ?? "");

    private async Task<string> BuildSchemaPromptAsync(Datasource ds)
    {
        var isPbi = IsPowerBi(ds.Type);
        var isRest = IsRestApi(ds.Type);
        var isFile = IsFileUrl(ds.Type);
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

        // File URL (CSV/XLSX): build schema from column headers in the parsed file
        if (isFile)
        {
            sb.AppendLine("You are connected to a CSV/Excel file datasource. Data is fetched and parsed automatically — do NOT generate SQL.");
            sb.AppendLine("When the user asks a data question, set the query field to \"FILE_URL\" and the system will fetch and parse the file.");
            sb.AppendLine("### File Data Fields:");
            try
            {
                var fileResult = await _queryService.ExecuteFileUrlAsync(ds, 5);
                if (fileResult.Success && fileResult.Data.Count > 0)
                {
                    var fields = fileResult.Data.First().Keys.ToList();
                    sb.AppendLine($"- **{ds.Name.Replace(" ", "_")}** (File): {string.Join(", ", fields)}");
                    sb.AppendLine($"- Sample row count: {fileResult.Data.Count}");
                    sb.AppendLine("### Sample Data:");
                    foreach (var row in fileResult.Data.Take(3))
                    {
                        var vals = row.Select(kv => $"{kv.Key}={kv.Value}");
                        sb.AppendLine($"  - {string.Join(", ", vals)}");
                    }
                }
                else
                {
                    sb.AppendLine("- Could not retrieve sample data from the file.");
                }
            }
            catch
            {
                sb.AppendLine("- Could not retrieve sample data from the file.");
            }
            sb.AppendLine();
            sb.AppendLine("Use these field names when suggesting charts. Always set query to \"FILE_URL\".");
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
                sb.AppendLine("### Aggregation / GROUP BY Rules (CRITICAL — violating these causes 'column is invalid in the select list' errors):");
                sb.AppendLine("- If the SELECT list contains ANY aggregate function (COUNT, SUM, AVG, MIN, MAX), then EVERY other non-aggregated column in the SELECT list MUST appear in the GROUP BY clause.");
                sb.AppendLine("- To return a standalone total/count from an UNRELATED table alongside row data, use a scalar subquery — NOT a CROSS JOIN with a single-row aggregate. ");
                sb.AppendLine("  CORRECT:   SELECT bv.[Database Version], bv.[VersionDate], (SELECT COUNT(*) FROM [SalesLT].[Address]) AS [TotalAddresses] FROM [dbo].[BuildVersion] bv");
                sb.AppendLine("  INCORRECT: SELECT bv.[Database Version], bv.[VersionDate], COUNT(a.AddressID) FROM [dbo].[BuildVersion] bv CROSS JOIN (SELECT COUNT(*) AS AddressID FROM [SalesLT].[Address]) a");
                sb.AppendLine("- Only join tables that have a meaningful relationship (foreign key or shared key). Do NOT CROSS JOIN unrelated tables just to combine metrics.");
                sb.AppendLine("- If uncertain whether to aggregate, prefer a plain SELECT without aggregates rather than mixing aggregated and non-aggregated columns.");
                sb.AppendLine("- When using GROUP BY, repeat the FULL bracketed expression (e.g., GROUP BY [SalesLT].[Product].[ProductCategoryID]) — do not reference SELECT aliases.");
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

        // Fallback: placeholder schema based on selected tables.
        // For Power BI, never emit the SQL-typed Customers/Orders/... placeholder —
        // those tables don't exist in the user's semantic model and make the AI
        // generate queries that look like they're hitting a different datasource.
        if (isPbi)
        {
            var pbiTables = string.IsNullOrEmpty(ds.SelectedTables)
                ? Array.Empty<string>()
                : ds.SelectedTables.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (pbiTables.Length > 0)
            {
                foreach (var t in pbiTables)
                    sb.AppendLine($"- '{t.Trim()}'");
            }
            else
            {
                sb.AppendLine("- Schema introspection unavailable. Use `EVALUATE INFO.TABLES()` and `EVALUATE INFO.COLUMNS()` to discover tables and columns before answering.");
            }
            sb.AppendLine();
            sb.AppendLine("Generate DAX queries (not SQL) for this Power BI model.");
            return sb.ToString();
        }

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

        // File URL (CSV/XLSX): use column names from the parsed file for suggestions
        if (IsFileUrl(ds.Type))
        {
            var fileDsName = ds.Name ?? "File";
            var fields = new List<string>();
            try
            {
                var fileResult = await _queryService.ExecuteFileUrlAsync(ds, 5);
                if (fileResult.Success && fileResult.Data.Count > 0)
                    fields = fileResult.Data.First().Keys.ToList();
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
                suggestions.Add(new { text = $"Show me all data from {fileDsName}", icon = "bi-file-earmark-spreadsheet" });
                suggestions.Add(new { text = $"What are the unique values of {firstField}?", icon = "bi-list-ul" });
                suggestions.Add(new { text = $"Summarize {numeric}", icon = "bi-graph-up" });
                suggestions.Add(new { text = $"Show the top 10 rows", icon = "bi-sort-down" });
                suggestions.Add(new { text = $"What columns are available?", icon = "bi-diagram-3" });
            }
            else
            {
                suggestions.Add(new { text = $"Show me all data from {fileDsName}", icon = "bi-file-earmark-spreadsheet" });
                suggestions.Add(new { text = $"What columns are available?", icon = "bi-diagram-3" });
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
    /// <summary>
    /// Optional tag identifying the originating feature. Use "chart_explain" when
    /// the request is made by the "Explain by AI" chart-insights flow so that the
    /// server can enforce the AllowsChartExplain plan gate before streaming.
    /// </summary>
    public string? Source { get; set; }
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
    // When true, flush the cached results for this datasource before executing so the
    // query hits the live database. Used by the “Refresh Cache” UI affordance.
    public bool NoCache { get; set; }
}

public class AnalyzeImageRequest
{
    public string? ImageDataUrl { get; set; }
    public string? Prompt { get; set; }
    public string? WorkspaceId { get; set; }
    public string? UserId { get; set; }
}

