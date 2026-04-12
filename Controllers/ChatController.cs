using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace ChatPortal2.Controllers;

[Authorize]
public class ChatController : Controller
{
    private readonly AppDbContext _db;
    private readonly CohereService _cohereService;
    private readonly JwtService _jwtService;
    private readonly IQueryExecutionService _queryService;
    private readonly ITokenBudgetService _tokenBudget;

    public ChatController(AppDbContext db, CohereService cohereService, JwtService jwtService, IQueryExecutionService queryService, ITokenBudgetService tokenBudget)
    {
        _db = db;
        _cohereService = cohereService;
        _jwtService = jwtService;
        _queryService = queryService;
        _tokenBudget = tokenBudget;
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

        // Check token budget for the organization
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";
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

        // Look up agent ? datasource for schema context
        string schemaContext = "";
        string datasourceIdentity = "";
        if (!string.IsNullOrEmpty(req.AgentId))
        {
            Agent? agent = null;
            if (int.TryParse(req.AgentId, out var agentIntId))
                agent = await _db.Agents.Include(a => a.Datasource).FirstOrDefaultAsync(a => a.Id == agentIntId);
            agent ??= await _db.Agents.Include(a => a.Datasource).FirstOrDefaultAsync(a => a.Guid == req.AgentId);

            if (agent?.Datasource != null)
            {
                schemaContext = await BuildSchemaPromptAsync(agent.Datasource);
                datasourceIdentity = $"\n\n## Active Datasource\nYou are currently connected to **{agent.Datasource.Name}** (Type: {agent.Datasource.Type}). " +
                    $"All queries you generate MUST target this specific datasource. " +
                    $"Use the exact table and column names from the schema provided below. " +
                    $"Do NOT assume or invent table/column names that are not in the schema.";
            }
        }

        // Inject workspace memories into system prompt
        var defaultPrompt = @"You are ChatPortal2's AI data assistant. When a user asks a data question:

1. **Understand Intent**: Determine what data the user wants.
2. **Generate Query**: Based on the connected datasource, generate the appropriate SQL/query.
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
Always be concise and actionable.";

        var effectiveSystemPrompt = req.SystemPrompt ?? defaultPrompt;
        if (!string.IsNullOrEmpty(datasourceIdentity))
            effectiveSystemPrompt += datasourceIdentity;
        if (!string.IsNullOrEmpty(schemaContext))
            effectiveSystemPrompt += "\n\n" + schemaContext;
     

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

    [AllowAnonymous]
    [HttpPost("/api/data/execute")]
    public async Task<IActionResult> ExecuteQuery([FromBody] ExecuteQueryRequest req)
    {
        var query = (req.Query ?? "").Trim();

        // Server-side read-only guard � block all write operations
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            query.ToUpperInvariant(), @"\s+", " ").Trim();
        var writeOps = new[] { "INSERT","UPDATE","DELETE","DROP","CREATE","ALTER",
                               "TRUNCATE","EXEC","EXECUTE","MERGE","CALL","GRANT",
                               "REVOKE","REPLACE","UPSERT","ATTACH","DETACH" };
        var firstToken = System.Text.RegularExpressions.Regex.Split(
            normalized.TrimStart(), @"[\s(;]+").FirstOrDefault() ?? "";
        if (writeOps.Contains(firstToken) ||
            writeOps.Any(kw => System.Text.RegularExpressions.Regex.IsMatch(
                normalized, $@"\b{kw}\b")))
        {
            return BadRequest(new
            {
                success = false,
                error = $"Write operation \"{firstToken}\" is not permitted. " +
                        "Only SELECT queries are allowed on this connection."
            });
        }

        // Look up the connected datasource and execute against it
        if (req.DatasourceId.HasValue && req.DatasourceId.Value > 0)
        {
            var ds = await _db.Datasources.FindAsync(req.DatasourceId.Value);
            if (ds != null && !string.IsNullOrWhiteSpace(ds.ConnectionString))
            {
                var result = await _queryService.ExecuteReadOnlyAsync(ds, query);
                return Ok(new { success = result.Success, data = result.Data, rowCount = result.RowCount, error = result.Error });
            }
        }

        // Fallback: no datasource connected � return informative error
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
            var wsId = await ResolveWorkspaceIdAsync(req.WorkspaceId);
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

    private async Task<string> BuildSchemaPromptAsync(Datasource ds)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Connected Datasource: {ds.Name} ({ds.Type})");
        sb.AppendLine("You are connected to this datasource. Generate SQL queries compatible with this database type.");
        sb.AppendLine("### Database Schema:");

        // Try real DB schema introspection
        if (!string.IsNullOrWhiteSpace(ds.ConnectionString))
        {
            try
            {
                var sql = GetSchemaIntrospectionQuery(ds.Type);
                if (sql != null)
                {
                    var result = await _queryService.ExecuteReadOnlyAsync(ds, sql);
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
                        sb.AppendLine("Always use exact table and column names from the schema above.");
                        sb.AppendLine($"Generate SQL appropriate for {ds.Type}.");
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
            ["Orders"] = "Orders (Id INT PK, CustomerId INT FK?Customers, OrderDate DATETIME, TotalAmount DECIMAL, Status NVARCHAR, Region NVARCHAR)",
            ["Products"] = "Products (Id INT PK, Name NVARCHAR, Category NVARCHAR, Price DECIMAL, StockQuantity INT)",
            ["Sales"] = "Sales (Id INT PK, ProductId INT FK?Products, CustomerId INT FK?Customers, Quantity INT, TotalRevenue DECIMAL, SaleDate DATETIME, Region NVARCHAR)",
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
        sb.AppendLine($"Generate SQL appropriate for {ds.Type}.");
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

        // Try real schema introspection for table names
        if (!string.IsNullOrWhiteSpace(ds.ConnectionString))
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
            return "SELECT t.TABLE_SCHEMA + '.' + t.TABLE_NAME as table_name, c.COLUMN_NAME as column_name, c.DATA_TYPE as data_type FROM INFORMATION_SCHEMA.TABLES t JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME WHERE t.TABLE_TYPE = 'BASE TABLE' ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION";
        if (t.Contains("Postgre", StringComparison.OrdinalIgnoreCase))
            return "SELECT t.table_name, c.column_name, c.data_type FROM information_schema.tables t JOIN information_schema.columns c ON c.table_name = t.table_name AND c.table_schema = t.table_schema WHERE t.table_schema = 'public' ORDER BY t.table_name, c.ordinal_position";
        if (t.Contains("MySQL", StringComparison.OrdinalIgnoreCase) || t.Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
            return "SELECT t.TABLE_NAME as table_name, c.COLUMN_NAME as column_name, c.DATA_TYPE as data_type FROM INFORMATION_SCHEMA.TABLES t JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_NAME = t.TABLE_NAME AND c.TABLE_SCHEMA = t.TABLE_SCHEMA WHERE t.TABLE_SCHEMA = DATABASE() ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";
        return null;
    }
}

public class SendMessageRequest
{
    public string? Message { get; set; }
    public string? SystemPrompt { get; set; }
    public string? WorkspaceId { get; set; }
    public string? AgentId { get; set; }
    public string? UserId { get; set; }
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

