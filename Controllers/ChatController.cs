using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace ChatPortal2.Controllers;

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
            req.SystemPrompt ?? "You are a helpful data assistant."))
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
            await _db.SaveChangesAsync();
        }
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
