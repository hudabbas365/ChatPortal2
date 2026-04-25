using AIInsights.Data;
using AIInsights.Models;
using AIInsights.SuperAdmin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.SuperAdmin.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public class DigestController : Controller
{
    private readonly AppDbContext _db;
    private readonly DigestSenderService _digestSender;

    public DigestController(AppDbContext db, DigestSenderService digestSender)
    {
        _db = db;
        _digestSender = digestSender;
    }

    private string? GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private async Task<bool> IsSuperAdminAsync()
    {
        if (!User.Claims.Any(c => c.Type == "role" && c.Value == "SuperAdmin"))
            return false;
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId)) return false;
        var user = await _db.Users.FindAsync(userId) as ApplicationUser;
        return user?.Role == "SuperAdmin";
    }

    [HttpGet("/superadmin/digest")]
    public async Task<IActionResult> Index()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var runs = await _db.DigestRuns
            .OrderByDescending(r => r.SentAt)
            .Take(12)
            .ToListAsync();

        return View("~/Views/Admin/Digest.cshtml", runs);
    }

    [HttpPost("/api/superadmin/digest/send-now")]
    public async Task<IActionResult> SendNow([FromBody] SendDigestRequest? req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        DateTime? weekStart = null;
        if (req?.WeekStart != null && DateTime.TryParse(req.WeekStart, out var parsed))
            weekStart = parsed.Date;

        await _digestSender.TrySendDigestAsync(
            weekStart ?? DigestSenderService.GetPreviousMonday(DateTime.UtcNow.Date),
            CancellationToken.None);

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Digest.SendNow",
            Description = $"SuperAdmin manually triggered digest for week starting {weekStart?.ToString("yyyy-MM-dd") ?? "previous week"}",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpGet("/api/superadmin/digest/preview")]
    public async Task<IActionResult> Preview([FromQuery] string? weekStart)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        DateTime start;
        if (!string.IsNullOrEmpty(weekStart) && DateTime.TryParse(weekStart, out var parsed))
            start = parsed.Date;
        else
            start = DigestSenderService.GetPreviousMonday(DateTime.UtcNow.Date);

        var end = start.AddDays(7);
        var html = await _digestSender.BuildDigestHtmlAsync(_db, start, end, CancellationToken.None);
        return Content(html, "text/html");
    }

    public class SendDigestRequest
    {
        public string? WeekStart { get; set; }
    }
}
