using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace ChatPortal2.Controllers;

[Authorize]
public class ChatController : Controller
{
    private readonly AppDbContext _db;
    private readonly CohereService _cohereService;
    private readonly JwtService _jwtService;

    public ChatController(AppDbContext db, CohereService cohereService, JwtService jwtService)
    {
        _db = db;
        _cohereService = cohereService;
        _jwtService = jwtService;
    }

    [HttpGet("/chat")]
    public IActionResult Index() => View();

    [HttpGet("/chat/embed")]
    public IActionResult Embed() => View();

    [HttpPost("/api/chat/send")]
    public async Task SendMessage([FromBody] SendMessageRequest req)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var history = new List<(string role, string content)>();
        if (req.WorkspaceId > 0)
        {
            var msgs = await _db.ChatMessages
                .Where(m => m.WorkspaceId == req.WorkspaceId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(10)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
            history = msgs.Select(m => (m.Role, m.Content)).ToList();
        }

        var fullResponse = new StringBuilder();
        await foreach (var chunk in _cohereService.StreamChatAsync(
            req.Message ?? "",
            history,
            req.SystemPrompt ?? @"You are ChatPortal2's AI data assistant. When a user asks a data question:

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
Always be concise and actionable."))
        {
            fullResponse.Append(chunk);
            var data = $"data: {Newtonsoft.Json.JsonConvert.SerializeObject(new { text = chunk })}\n\n";
            await Response.WriteAsync(data);
            await Response.Body.FlushAsync();
        }

        await Response.WriteAsync("data: [DONE]\n\n");
        await Response.Body.FlushAsync();

        // Save messages to DB
        if (req.WorkspaceId > 0 && !string.IsNullOrEmpty(req.UserId))
        {
            _db.ChatMessages.Add(new ChatMessage
            {
                Role = "user",
                Content = req.Message ?? "",
                WorkspaceId = req.WorkspaceId,
                UserId = req.UserId
            });
            _db.ChatMessages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = fullResponse.ToString(),
                WorkspaceId = req.WorkspaceId,
                UserId = req.UserId
            });
            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "agent_execution",
                Description = $"Chat message sent in workspace {req.WorkspaceId}.",
                UserId = req.UserId
            });
            await _db.SaveChangesAsync();
        }
    }

    [HttpPost("/api/data/execute")]
    public IActionResult ExecuteQuery([FromBody] ExecuteQueryRequest req)
    {
        // Placeholder: returns sample data for the query
        // In production this would execute against the bound datasource
        var sampleData = new List<Dictionary<string, object>>
        {
            new() { ["region"] = "North", ["total_revenue"] = 142500 },
            new() { ["region"] = "South", ["total_revenue"] = 98700 },
            new() { ["region"] = "East",  ["total_revenue"] = 211300 },
            new() { ["region"] = "West",  ["total_revenue"] = 175600 }
        };
        return Ok(new { success = true, data = sampleData, rowCount = sampleData.Count });
    }

    [HttpPost("/api/chat/pin")]
    public async Task<IActionResult> PinResult([FromBody] PinRequest req)
    {
        var pinned = new PinnedResult
        {
            DatasetName = req.DatasetName ?? "pinned",
            JsonData = req.JsonData ?? "[]",
            ChatMessageId = req.ChatMessageId,
            WorkspaceId = req.WorkspaceId,
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
}

public class SendMessageRequest
{
    public string? Message { get; set; }
    public string? SystemPrompt { get; set; }
    public int WorkspaceId { get; set; }
    public string? UserId { get; set; }
}

public class PinRequest
{
    public string? DatasetName { get; set; }
    public string? JsonData { get; set; }
    public int ChatMessageId { get; set; }
    public int WorkspaceId { get; set; }
    public string? UserId { get; set; }
}

public class ExecuteQueryRequest
{
    public string? Query { get; set; }
    public int? DatasourceId { get; set; }
    public string? UserId { get; set; }
}
