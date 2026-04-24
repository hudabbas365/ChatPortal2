using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace AIInsights.SuperAdmin.Services;

/// <summary>
/// Singleton service containing the digest-building and sending logic.
/// Used by both WeeklyDigestService (background) and DigestController (manual trigger).
/// </summary>
public class DigestSenderService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DigestSenderService> _logger;

    public DigestSenderService(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<DigestSenderService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    public async Task TrySendDigestAsync(DateTime? weekStartOverride, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var weekStart = weekStartOverride?.Date ?? GetPreviousMonday(DateTime.UtcNow.Date);
        var weekEnd = weekStart.AddDays(7);
        var weekIso = GetIsoWeekLabel(weekStart);

        // Idempotency: only send once per week unless forced
        if (!weekStartOverride.HasValue)
        {
            var alreadySent = await db.DigestRuns.AnyAsync(d => d.RunWeekIso == weekIso, ct);
            if (alreadySent) return;
        }

        var recipients = _config.GetSection("SuperAdmin:DigestRecipients").Get<string[]>();
        if (recipients == null || recipients.Length == 0)
        {
            _logger.LogInformation("No digest recipients configured, skipping digest for {Week}", weekIso);
            return;
        }

        var html = await BuildDigestHtmlAsync(db, weekStart, weekEnd, ct);
        await SendDigestEmailAsync(recipients, weekStart, weekEnd.AddDays(-1), html);

        db.DigestRuns.Add(new DigestRun { RunWeekIso = weekIso, SentAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Weekly digest sent for {Week}", weekIso);
    }

    public async Task<string> BuildDigestHtmlAsync(AppDbContext db, DateTime weekStart, DateTime weekEnd, CancellationToken ct)
    {
        var newOrgsThisWeek = await db.Organizations
            .Where(o => o.CreatedAt >= weekStart && o.CreatedAt < weekEnd)
            .CountAsync(ct);

        var top5NewOrgs = await db.Organizations
            .Where(o => o.CreatedAt >= weekStart && o.CreatedAt < weekEnd)
            .OrderByDescending(o => o.Users.Count)
            .Take(5)
            .Select(o => new { o.Name, UserCount = o.Users.Count })
            .ToListAsync(ct);

        var newUsersThisWeek = await db.Users
            .Where(u => u.CreatedAt >= weekStart && u.CreatedAt < weekEnd)
            .CountAsync(ct);

        var failedIntegrations = await db.ActivityLogs
            .Where(l => l.Action == "Integration.Down" && l.CreatedAt >= weekStart && l.CreatedAt < weekEnd)
            .CountAsync(ct);

        var top5Actions = await db.ActivityLogs
            .Where(l => l.CreatedAt >= weekStart && l.CreatedAt < weekEnd && l.UserId != null)
            .GroupBy(l => l.Action)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var activeUsers = await db.ActivityLogs
            .Where(l => l.CreatedAt >= weekStart && l.CreatedAt < weekEnd && l.UserId != null && l.UserId != "")
            .Select(l => l.UserId)
            .Distinct()
            .CountAsync(ct);

        var top5Orgs = await db.ActivityLogs
            .Where(l => l.CreatedAt >= weekStart && l.CreatedAt < weekEnd && l.OrganizationId != null)
            .GroupBy(l => l.OrganizationId)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Join(db.Organizations, g => g.Key, o => o.Id, (g, o) => new { o.Name, MsgCount = g.Count() })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/>
<style>
  body {{ font-family: Arial, sans-serif; background: #f4f4f4; color: #333; }}
  .container {{ max-width: 620px; margin: 20px auto; background: #fff; border-radius: 8px; overflow: hidden; }}
  .header {{ background: #1e3a5f; color: #fff; padding: 24px 32px; }}
  .header h1 {{ margin: 0; font-size: 1.4rem; }}
  .header p {{ margin: 4px 0 0; color: #aac4e0; font-size: .85rem; }}
  .section {{ padding: 20px 32px; border-bottom: 1px solid #eee; }}
  .section h2 {{ font-size: 1rem; color: #1e3a5f; margin: 0 0 12px; }}
  .stat-row {{ display: flex; gap: 16px; flex-wrap: wrap; margin-bottom: 8px; }}
  .stat {{ background: #f0f4f8; border-radius: 6px; padding: 12px 18px; flex: 1; min-width: 100px; }}
  .stat .val {{ font-size: 1.6rem; font-weight: 700; color: #1e3a5f; }}
  .stat .lbl {{ font-size: .75rem; color: #666; }}
  table {{ width: 100%; border-collapse: collapse; font-size: .85rem; }}
  th {{ text-align: left; color: #666; padding: 6px 8px; border-bottom: 1px solid #eee; }}
  td {{ padding: 6px 8px; border-bottom: 1px solid #f4f4f4; }}
  .footer {{ padding: 16px 32px; font-size: .75rem; color: #999; text-align: center; }}
</style>
</head>
<body>
<div class='container'>
  <div class='header'>
    <h1>AIInsights365 Weekly Digest</h1>
    <p>{weekStart:MMM d} – {weekEnd.AddDays(-1):MMM d, yyyy}</p>
  </div>
  <div class='section'>
    <h2>📊 At a Glance</h2>
    <div class='stat-row'>
      <div class='stat'><div class='val'>{newOrgsThisWeek}</div><div class='lbl'>New Orgs</div></div>
      <div class='stat'><div class='val'>{newUsersThisWeek}</div><div class='lbl'>New Users</div></div>
      <div class='stat'><div class='val'>{activeUsers}</div><div class='lbl'>Active Users (WAU)</div></div>
      <div class='stat'><div class='val'>{failedIntegrations}</div><div class='lbl'>Integration Failures</div></div>
    </div>
  </div>
  <div class='section'>
    <h2>🏢 Top New Organizations</h2>
    <table>
      <tr><th>Name</th><th>Users</th></tr>");
        foreach (var o in top5NewOrgs)
            sb.Append($"<tr><td>{System.Net.WebUtility.HtmlEncode(o.Name)}</td><td>{o.UserCount}</td></tr>");
        if (top5NewOrgs.Count == 0) sb.Append("<tr><td colspan='2'>No new organizations this week.</td></tr>");
        sb.Append($@"
    </table>
  </div>
  <div class='section'>
    <h2>🏆 Most Active Organizations</h2>
    <table>
      <tr><th>Name</th><th>Actions</th></tr>");
        foreach (var o in top5Orgs)
            sb.Append($"<tr><td>{System.Net.WebUtility.HtmlEncode(o.Name)}</td><td>{o.MsgCount}</td></tr>");
        if (top5Orgs.Count == 0) sb.Append("<tr><td colspan='2'>No activity data.</td></tr>");
        sb.Append($@"
    </table>
  </div>
  <div class='section'>
    <h2>🔑 Top SuperAdmin Actions</h2>
    <table>
      <tr><th>Action</th><th>Count</th></tr>");
        foreach (var a in top5Actions)
            sb.Append($"<tr><td>{System.Net.WebUtility.HtmlEncode(a.Action)}</td><td>{a.Count}</td></tr>");
        if (top5Actions.Count == 0) sb.Append("<tr><td colspan='2'>No admin actions this week.</td></tr>");
        sb.Append($@"
    </table>
  </div>
  <div class='footer'>
    This digest was automatically generated by AIInsights365 SuperAdmin.<br/>
    Generated at {DateTime.UtcNow:O}
  </div>
</div>
</body>
</html>");
        return sb.ToString();
    }

    private async Task SendDigestEmailAsync(string[] recipients, DateTime weekStart, DateTime weekEnd, string html)
    {
        var host = _config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host)) return;

        try
        {
            var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
            var from = _config["Smtp:From"] ?? "noreply@aiinsights365.net";
            var username = _config["Smtp:Username"];
            var password = _config["Smtp:Password"];
            var enableSsl = _config.GetValue<bool>("Smtp:EnableSsl", true);

            using var smtp = new System.Net.Mail.SmtpClient(host, port);
            smtp.EnableSsl = enableSsl;
            if (!string.IsNullOrWhiteSpace(username))
                smtp.Credentials = new System.Net.NetworkCredential(username, password);

            var subject = $"AIInsights365 weekly digest — {weekStart:MMM d} – {weekEnd:MMM d, yyyy}";
            foreach (var email in recipients)
            {
                var msg = new System.Net.Mail.MailMessage(from, email, subject, html);
                msg.IsBodyHtml = true;
                await smtp.SendMailAsync(msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send weekly digest email.");
        }
    }

    public static DateTime GetPreviousMonday(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        var diff = dayOfWeek == 0 ? 6 : dayOfWeek - 1;
        return date.AddDays(-7 - diff);
    }

    public static string GetIsoWeekLabel(DateTime monday)
    {
        var cal = CultureInfo.InvariantCulture.Calendar;
        var weekNum = cal.GetWeekOfYear(monday, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        return $"{monday.Year}-W{weekNum:D2}";
    }
}
