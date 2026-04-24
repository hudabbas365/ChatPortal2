using AIInsights.Data;
using AIInsights.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using System.Text;

namespace AIInsights.SuperAdmin.Controllers;

[Authorize]
public class InsightsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;

    public InsightsController(AppDbContext db, IMemoryCache cache, IConfiguration config)
    {
        _db = db;
        _cache = cache;
        _config = config;
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

    private double GetCostPer1kTokens() =>
        _config.GetValue<double>("AiPricing:default:costPer1k", 0.002);

    private double ComputeCost(long tokens) =>
        (tokens / 1000.0) * GetCostPer1kTokens();

    private (DateTime from, DateTime to) ParseRange(string range)
    {
        var now = DateTime.UtcNow;
        return range switch
        {
            "7d"  => (now.AddDays(-7), now),
            "90d" => (now.AddDays(-90), now),
            "ytd" => (new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), now),
            _     => (now.AddDays(-30), now) // 30d default
        };
    }

    // ──────────────────────────────────────────────────────────
    // D14 — User Search
    // ──────────────────────────────────────────────────────────

    [HttpGet("/superadmin/users")]
    public async Task<IActionResult> UserSearch([FromQuery] string? search)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var query = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u => u.Email!.ToLower().Contains(s) || u.FullName.ToLower().Contains(s));
        }

        var users = await query
            .OrderBy(u => u.Email)
            .Take(100)
            .Select(u => new UserSearchItem
            {
                Id = u.Id,
                Email = u.Email ?? "",
                FullName = u.FullName,
                Role = u.Role,
                OrganizationId = u.OrganizationId,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        var orgIds = users.Where(u => u.OrganizationId.HasValue)
                          .Select(u => u.OrganizationId!.Value)
                          .Distinct()
                          .ToList();
        var orgs = await _db.Organizations.AsNoTracking()
                            .Where(o => orgIds.Contains(o.Id))
                            .Select(o => new { o.Id, o.Name })
                            .ToDictionaryAsync(o => o.Id, o => o.Name);
        foreach (var u in users)
            if (u.OrganizationId.HasValue && orgs.TryGetValue(u.OrganizationId.Value, out var n))
                u.OrgName = n;

        ViewBag.Search = search;
        return View("~/Views/Admin/UserSearch.cshtml", users);
    }

    // ──────────────────────────────────────────────────────────
    // D14 — User Activity Timeline
    // ──────────────────────────────────────────────────────────

    [HttpGet("/superadmin/users/{id}/timeline")]
    public async Task<IActionResult> UserTimeline(string id,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var user = await _db.Users.AsNoTracking()
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        const int pageSize = 50;
        var now = DateTime.UtcNow;
        var fromDate = from ?? now.AddDays(-30);
        var toDate   = to   ?? now;

        var logQuery    = _db.ActivityLogs.AsNoTracking().Where(l => l.UserId == id);
        var windowQuery = logQuery.Where(l => l.CreatedAt >= fromDate && l.CreatedAt <= toDate);

        var totalCount  = await logQuery.CountAsync();
        var windowCount = await windowQuery.CountAsync();

        var logs = await windowQuery
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var lastSeen      = await logQuery.MaxAsync(l => (DateTime?)l.CreatedAt);
        var totalMessages = await _db.ChatMessages.AsNoTracking().CountAsync(m => m.UserId == id);
        var totalTokens   = await _db.TokenUsages.AsNoTracking()
                                .Where(t => t.UserId == id)
                                .SumAsync(t => (long?)t.TokensUsed) ?? 0;

        var subscription = await _db.SubscriptionPlans.AsNoTracking()
                               .FirstOrDefaultAsync(s => s.UserId == id);

        var vm = new UserTimelineViewModel
        {
            User              = user,
            OrgName           = user.Organization?.Name ?? "—",
            OrgId             = user.OrganizationId,
            Plan              = subscription?.Plan.ToString() ?? user.Organization?.Plan.ToString() ?? "Free",
            TotalActionsAllTime  = totalCount,
            TotalActionsInWindow = windowCount,
            LastSeen          = lastSeen,
            TotalMessages     = totalMessages,
            TotalTokens       = totalTokens,
            Logs              = logs,
            Page              = page,
            PageSize          = pageSize,
            From              = fromDate,
            To                = toDate
        };

        return View("~/Views/Admin/UserTimeline.cshtml", vm);
    }

    // ──────────────────────────────────────────────────────────
    // D15 — Token Usage Dashboard (MVC view)
    // ──────────────────────────────────────────────────────────

    [HttpGet("/superadmin/insights/token-usage")]
    public async Task<IActionResult> TokenUsage(
        [FromQuery] string range  = "30d",
        [FromQuery] int? orgId    = null,
        [FromQuery] string? userId = null)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var orgs = await _db.Organizations.AsNoTracking()
                            .OrderBy(o => o.Name)
                            .Select(o => new { o.Id, o.Name })
                            .ToListAsync();
        ViewBag.Orgs   = orgs;
        ViewBag.Range  = range;
        ViewBag.OrgId  = orgId;
        ViewBag.UserId = userId;

        return View("~/Views/Admin/TokenUsage.cshtml");
    }

    // ──────────────────────────────────────────────────────────
    // D15 — Token Usage JSON endpoint
    // ──────────────────────────────────────────────────────────

    [HttpGet("/api/superadmin/insights/token-usage")]
    public async Task<IActionResult> TokenUsageJson(
        [FromQuery] string range  = "30d",
        [FromQuery] int? orgId    = null,
        [FromQuery] string? userId = null)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var cacheKey = $"insights:token-usage:{range}:{orgId}:{userId}";
        if (_cache.TryGetValue(cacheKey, out object? cached))
            return Ok(cached);

        var (from, to) = ParseRange(range);
        double costRate = GetCostPer1kTokens();

        var query = _db.TokenUsages.AsNoTracking()
                       .Where(t => t.CreatedAt >= from && t.CreatedAt <= to);
        if (orgId.HasValue)          query = query.Where(t => t.OrganizationId == orgId.Value);
        if (!string.IsNullOrEmpty(userId)) query = query.Where(t => t.UserId == userId);

        var rows = await query
            .Select(t => new { t.OrganizationId, t.UserId, t.TokensUsed, t.CreatedAt })
            .ToListAsync();

        long totalTokens = rows.Sum(r => (long)r.TokensUsed);
        double totalCost = (totalTokens / 1000.0) * costRate;

        var daily = rows
            .GroupBy(r => r.CreatedAt.Date)
            .Select(g => new
            {
                date   = g.Key.ToString("yyyy-MM-dd"),
                tokens = g.Sum(r => (long)r.TokensUsed),
                cost   = Math.Round((g.Sum(r => (long)r.TokensUsed) / 1000.0) * costRate, 4)
            })
            .OrderBy(d => d.date)
            .ToList();

        var orgIds = rows.Select(r => r.OrganizationId).Distinct().ToList();
        var orgNames = await _db.Organizations.AsNoTracking()
                                .Where(o => orgIds.Contains(o.Id))
                                .ToDictionaryAsync(o => o.Id, o => o.Name);
        var topOrgs = rows
            .GroupBy(r => r.OrganizationId)
            .Select(g => new
            {
                orgId   = g.Key,
                orgName = orgNames.TryGetValue(g.Key, out var n) ? n : $"Org #{g.Key}",
                tokens  = g.Sum(r => (long)r.TokensUsed),
                cost    = Math.Round((g.Sum(r => (long)r.TokensUsed) / 1000.0) * costRate, 4)
            })
            .OrderByDescending(g => g.tokens)
            .Take(10)
            .ToList();

        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var userEmails = await _db.Users.AsNoTracking()
                                  .Where(u => userIds.Contains(u.Id))
                                  .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.Id);
        var topUsers = rows
            .GroupBy(r => r.UserId)
            .Select(g => new
            {
                userId    = g.Key,
                userEmail = userEmails.TryGetValue(g.Key, out var e) ? e : g.Key,
                tokens    = g.Sum(r => (long)r.TokensUsed),
                cost      = Math.Round((g.Sum(r => (long)r.TokensUsed) / 1000.0) * costRate, 4)
            })
            .OrderByDescending(g => g.tokens)
            .Take(10)
            .ToList();

        var result = new
        {
            totals = new
            {
                // TokenUsage model only stores total tokens (no prompt/completion split).
                // We estimate the split at 60% prompt / 40% completion as a reasonable
                // industry approximation for chat workloads; adjust if actual data becomes available.
                promptTokens     = (long)(totalTokens * 0.6),
                completionTokens = (long)(totalTokens * 0.4),
                totalTokens,
                estimatedCost    = Math.Round(totalCost, 4)
            },
            daily,
            topOrgs,
            topUsers
        };

        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────
    // D15 — Token Usage CSV Export
    // ──────────────────────────────────────────────────────────

    [HttpGet("/superadmin/insights/token-usage/export.csv")]
    public async Task<IActionResult> TokenUsageExport(
        [FromQuery] string range  = "30d",
        [FromQuery] int? orgId    = null,
        [FromQuery] string? userId = null)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var (from, to) = ParseRange(range);
        double costRate = GetCostPer1kTokens();

        var query = _db.TokenUsages.AsNoTracking()
                       .Where(t => t.CreatedAt >= from && t.CreatedAt <= to);
        if (orgId.HasValue)               query = query.Where(t => t.OrganizationId == orgId.Value);
        if (!string.IsNullOrEmpty(userId)) query = query.Where(t => t.UserId == userId);

        var rows = await query
            .Select(t => new { t.OrganizationId, t.UserId, t.TokensUsed, t.CreatedAt })
            .ToListAsync();

        var daily = rows
            .GroupBy(r => r.CreatedAt.Date)
            .Select(g => new
            {
                Date   = g.Key.ToString("yyyy-MM-dd"),
                Tokens = g.Sum(r => (long)r.TokensUsed),
                Cost   = Math.Round((g.Sum(r => (long)r.TokensUsed) / 1000.0) * costRate, 4)
            })
            .OrderBy(d => d.Date);

        var sb = new StringBuilder();
        sb.AppendLine("Date,Tokens,EstimatedCost");
        foreach (var d in daily)
            sb.AppendLine($"{d.Date},{d.Tokens},{d.Cost}");

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "token-usage.csv");
    }

    // ──────────────────────────────────────────────────────────
    // D16 — AI Spend vs Revenue (MVC view)
    // ──────────────────────────────────────────────────────────

    [HttpGet("/superadmin/insights/margin")]
    public async Task<IActionResult> Margin([FromQuery] string range = "30d")
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        ViewBag.Range = range;
        return View("~/Views/Admin/Margin.cshtml");
    }

    // ──────────────────────────────────────────────────────────
    // D16 — Margin JSON endpoint
    // ──────────────────────────────────────────────────────────

    [HttpGet("/api/superadmin/insights/margin")]
    public async Task<IActionResult> MarginJson([FromQuery] string range = "30d")
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var (from, to) = ParseRange(range);
        double costRate = GetCostPer1kTokens();

        var orgs = await _db.Organizations.AsNoTracking()
                            .Select(o => new { o.Id, o.Name, Plan = o.Plan.ToString() })
                            .ToListAsync();
        var orgIds = orgs.Select(o => o.Id).ToList();

        var tokenByOrg = await _db.TokenUsages.AsNoTracking()
            .Where(t => t.CreatedAt >= from && t.CreatedAt <= to)
            .GroupBy(t => t.OrganizationId)
            .Select(g => new { OrgId = g.Key, Tokens = g.Sum(t => (long)t.TokensUsed) })
            .ToListAsync();
        var spendByOrg = tokenByOrg.ToDictionary(t => t.OrgId, t => (t.Tokens / 1000.0) * costRate);

        var revenueByOrg = await _db.PaymentRecords.AsNoTracking()
            .Where(p => orgIds.Contains(p.OrganizationId)
                     && p.CreatedAt >= from && p.CreatedAt <= to
                     && p.Status == "succeeded")
            .GroupBy(p => p.OrganizationId)
            .Select(g => new { OrgId = g.Key, Revenue = (double)g.Sum(p => p.Amount) })
            .ToListAsync();
        var revByOrg = revenueByOrg.ToDictionary(r => r.OrgId, r => r.Revenue);

        var result = orgs.Select(o =>
        {
            double spend   = spendByOrg.TryGetValue(o.Id, out var s) ? s : 0;
            double revenue = revByOrg.TryGetValue(o.Id, out var r) ? r : 0;
            double margin  = revenue - spend;
            double marginPct = revenue > 0 ? Math.Round(margin / revenue * 100, 1) : 0;
            return new
            {
                orgId    = o.Id,
                orgName  = o.Name,
                plan     = o.Plan,
                spend    = Math.Round(spend, 4),
                revenue  = Math.Round(revenue, 2),
                margin   = Math.Round(margin, 2),
                marginPct
            };
        })
        .OrderBy(o => o.margin)
        .ToList();

        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────
    // D16 — Margin CSV Export
    // ──────────────────────────────────────────────────────────

    [HttpGet("/superadmin/insights/margin/export.csv")]
    public async Task<IActionResult> MarginExport([FromQuery] string range = "30d")
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var (from, to) = ParseRange(range);
        double costRate = GetCostPer1kTokens();

        var orgs = await _db.Organizations.AsNoTracking()
                            .Select(o => new { o.Id, o.Name, Plan = o.Plan.ToString() })
                            .ToListAsync();
        var orgIds = orgs.Select(o => o.Id).ToList();

        var tokenByOrg = await _db.TokenUsages.AsNoTracking()
            .Where(t => t.CreatedAt >= from && t.CreatedAt <= to)
            .GroupBy(t => t.OrganizationId)
            .Select(g => new { OrgId = g.Key, Tokens = g.Sum(t => (long)t.TokensUsed) })
            .ToListAsync();
        var spendByOrg = tokenByOrg.ToDictionary(t => t.OrgId, t => (t.Tokens / 1000.0) * costRate);

        var revenueByOrg = await _db.PaymentRecords.AsNoTracking()
            .Where(p => orgIds.Contains(p.OrganizationId)
                     && p.CreatedAt >= from && p.CreatedAt <= to
                     && p.Status == "succeeded")
            .GroupBy(p => p.OrganizationId)
            .Select(g => new { OrgId = g.Key, Revenue = (double)g.Sum(p => p.Amount) })
            .ToListAsync();
        var revByOrg = revenueByOrg.ToDictionary(r => r.OrgId, r => r.Revenue);

        var sb = new StringBuilder();
        sb.AppendLine("OrgId,OrgName,Plan,Spend,Revenue,Margin,MarginPct");
        foreach (var o in orgs)
        {
            double spend   = spendByOrg.TryGetValue(o.Id, out var s) ? s : 0;
            double revenue = revByOrg.TryGetValue(o.Id, out var r) ? r : 0;
            double margin  = revenue - spend;
            double marginPct = revenue > 0 ? Math.Round(margin / revenue * 100, 1) : 0;
            var safeName   = o.Name.Replace("\"", "\"\""); // escape double-quotes
            sb.AppendLine($"{o.Id},\"{safeName}\",{o.Plan},{Math.Round(spend, 4)},{Math.Round(revenue, 2)},{Math.Round(margin, 2)},{marginPct}");
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "margin.csv");
    }

    // ──────────────────────────────────────────────────────────
    // D17 — Churn-Risk List
    // ──────────────────────────────────────────────────────────

    [HttpGet("/superadmin/insights/churn-risk")]
    public async Task<IActionResult> ChurnRisk()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var cutoff = DateTime.UtcNow.AddDays(-14);
        var now    = DateTime.UtcNow;

        var paidOrgs = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsBlocked
                     && (o.Plan == PlanType.Professional || o.Plan == PlanType.Enterprise))
            .ToListAsync();

        var orgIds = paidOrgs.Select(o => o.Id).ToList();

        var usersByOrg = await _db.Users.AsNoTracking()
            .Where(u => u.OrganizationId.HasValue && orgIds.Contains(u.OrganizationId.Value))
            .Select(u => new { u.Id, u.OrganizationId })
            .ToListAsync();

        var userIdsByOrg = usersByOrg
            .GroupBy(u => u.OrganizationId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(u => u.Id).ToList());
        var allUserIds = usersByOrg.Select(u => u.Id).ToList();

        var lastActivityByUser = await _db.ActivityLogs.AsNoTracking()
            .Where(l => allUserIds.Contains(l.UserId))
            .GroupBy(l => l.UserId)
            .Select(g => new { UserId = g.Key, LastAt = g.Max(l => l.CreatedAt) })
            .ToListAsync();
        var lastActDict = lastActivityByUser.ToDictionary(l => l.UserId, l => l.LastAt);

        var lastMsgByUser = await _db.ChatMessages.AsNoTracking()
            .Where(m => allUserIds.Contains(m.UserId))
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, LastAt = g.Max(m => m.CreatedAt) })
            .ToListAsync();
        var lastMsgDict = lastMsgByUser.ToDictionary(l => l.UserId, l => l.LastAt);

        var openTicketsByOrg = await _db.SupportTickets.AsNoTracking()
            .Where(t => t.OrganizationId.HasValue && orgIds.Contains(t.OrganizationId.Value) && t.Status == "Open")
            .GroupBy(t => t.OrganizationId!.Value)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToListAsync();
        var ticketsByOrg = openTicketsByOrg.ToDictionary(t => t.OrgId, t => t.Count);

        var lastPaymentByOrg = await _db.PaymentRecords.AsNoTracking()
            .Where(p => orgIds.Contains(p.OrganizationId) && p.Status == "succeeded")
            .GroupBy(p => p.OrganizationId)
            .Select(g => new
            {
                OrgId      = g.Key,
                LastAt     = g.Max(p => p.CreatedAt),
                LastAmount = g.OrderByDescending(p => p.CreatedAt).Select(p => p.Amount).FirstOrDefault()
            })
            .ToListAsync();
        var paymentByOrg = lastPaymentByOrg.ToDictionary(p => p.OrgId);

        var risks = new List<ChurnRiskItem>();
        foreach (var org in paidOrgs)
        {
            var uids = userIdsByOrg.TryGetValue(org.Id, out var ids) ? ids : new List<string>();

            DateTime? lastActivity = uids
                .Select(uid => lastActDict.TryGetValue(uid, out var t) ? t : (DateTime?)null)
                .Where(t => t.HasValue)
                .DefaultIfEmpty()
                .Max();

            DateTime? lastMsg = uids
                .Select(uid => lastMsgDict.TryGetValue(uid, out var t) ? t : (DateTime?)null)
                .Where(t => t.HasValue)
                .DefaultIfEmpty()
                .Max();

            bool noRecentActivity = !lastActivity.HasValue || lastActivity.Value < cutoff;
            bool noRecentMsg      = !lastMsg.HasValue      || lastMsg.Value < cutoff;

            if (!noRecentActivity && !noRecentMsg) continue;

            int daysSince   = lastActivity.HasValue ? (int)(now - lastActivity.Value).TotalDays : 999;
            int openTickets = ticketsByOrg.TryGetValue(org.Id, out var tc) ? tc : 0;
            int riskScore   = Math.Min(100, daysSince * 5 + openTickets * 10);

            var payment    = paymentByOrg.TryGetValue(org.Id, out var p) ? p : null;
            decimal mrrEst = payment?.LastAmount ?? (org.Plan == PlanType.Professional
                ? PlanPricing.ProPricePerUser * Math.Max(1, org.PurchasedProfessionalLicenses)
                : PlanPricing.EnterprisePricePerUser * Math.Max(1, org.PurchasedEnterpriseLicenses));

            risks.Add(new ChurnRiskItem
            {
                OrgId              = org.Id,
                OrgName            = org.Name,
                Plan               = org.Plan.ToString(),
                LastActivityAt     = lastActivity,
                DaysSinceActivity  = daysSince,
                LastPaymentAt      = payment?.LastAt,
                MrrEstimate        = mrrEst,
                OpenSupportTickets = openTickets,
                RiskScore          = riskScore
            });
        }

        return View("~/Views/Admin/ChurnRisk.cshtml",
                    risks.OrderByDescending(r => r.RiskScore).ToList());
    }

    // ──────────────────────────────────────────────────────────
    // D17 — Churn-Risk CSV Export
    // ──────────────────────────────────────────────────────────

    [HttpGet("/superadmin/insights/churn-risk/export.csv")]
    public async Task<IActionResult> ChurnRiskExport()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var viewResult = await ChurnRisk();
        if (viewResult is not ViewResult vr || vr.Model is not List<ChurnRiskItem> items)
            return StatusCode(500);

        var sb = new StringBuilder();
        sb.AppendLine("OrgId,OrgName,Plan,RiskScore,DaysSinceActivity,LastActivityAt,LastPaymentAt,MrrEstimate,OpenTickets");
        foreach (var r in items)
        {
            var safeName = r.OrgName.Replace("\"", "\"\"");
            sb.AppendLine($"{r.OrgId},\"{safeName}\",{r.Plan},{r.RiskScore},{r.DaysSinceActivity}," +
                          $"{r.LastActivityAt?.ToString("yyyy-MM-dd") ?? ""},{r.LastPaymentAt?.ToString("yyyy-MM-dd") ?? ""},{r.MrrEstimate},{r.OpenSupportTickets}");
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "churn-risk.csv");
    }

    // ──────────────────────────────────────────────────────────
    // D18 — Org Health Score (MVC view)
    // ──────────────────────────────────────────────────────────

    [HttpGet("/superadmin/insights/health")]
    public async Task<IActionResult> OrgHealth()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        return View("~/Views/Admin/OrgHealth.cshtml");
    }

    // ──────────────────────────────────────────────────────────
    // D18 — Org Health JSON endpoint
    // ──────────────────────────────────────────────────────────

    [HttpGet("/api/superadmin/insights/health")]
    public async Task<IActionResult> OrgHealthJson()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        const string cacheKey = "insights:health";
        if (_cache.TryGetValue(cacheKey, out object? cached))
            return Ok(cached);

        var result = await ComputeHealthScoresAsync();
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────
    // Health score computation
    // ──────────────────────────────────────────────────────────

    private async Task<List<OrgHealthItem>> ComputeHealthScoresAsync()
    {
        var now          = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyDaysAgo = now.AddDays(-30);
        var sixtyDaysAgo  = now.AddDays(-60);

        var orgs = await _db.Organizations.AsNoTracking()
            .Where(o => !o.IsBlocked)
            .Select(o => new { o.Id, o.Name, o.Plan })
            .ToListAsync();

        var orgIds = orgs.Select(o => o.Id).ToList();

        var usersByOrg = await _db.Users.AsNoTracking()
            .Where(u => u.OrganizationId.HasValue && orgIds.Contains(u.OrganizationId.Value))
            .Select(u => new { u.Id, OrgId = u.OrganizationId!.Value })
            .ToListAsync();

        var userMap    = usersByOrg.GroupBy(u => u.OrgId)
                                   .ToDictionary(g => g.Key, g => g.Select(u => u.Id).ToList());
        var allUserIds = usersByOrg.Select(u => u.Id).ToList();

        var activeUsersLast7 = await _db.ActivityLogs.AsNoTracking()
            .Where(l => allUserIds.Contains(l.UserId) && l.CreatedAt >= sevenDaysAgo)
            .Select(l => l.UserId)
            .Distinct()
            .ToListAsync();
        var activeUserSet = new HashSet<string>(activeUsersLast7);

        var msgsLast7 = await _db.ChatMessages.AsNoTracking()
            .Where(m => allUserIds.Contains(m.UserId) && m.CreatedAt >= sevenDaysAgo)
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync();
        var msgsByUser = msgsLast7.ToDictionary(m => m.UserId, m => m.Count);

        var openTickets = await _db.SupportTickets.AsNoTracking()
            .Where(t => t.OrganizationId.HasValue && orgIds.Contains(t.OrganizationId.Value) && t.Status == "Open")
            .GroupBy(t => t.OrganizationId!.Value)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToListAsync();
        var ticketMap = openTickets.ToDictionary(t => t.OrgId, t => t.Count);

        var lastPayments = await _db.PaymentRecords.AsNoTracking()
            .Where(p => orgIds.Contains(p.OrganizationId) && p.Status == "succeeded")
            .GroupBy(p => p.OrganizationId)
            .Select(g => new { OrgId = g.Key, LastAt = g.Max(p => p.CreatedAt) })
            .ToListAsync();
        var paymentMap = lastPayments.ToDictionary(p => p.OrgId, p => p.LastAt);

        // Dashboards per org — via Workspace join
        var dashboardsByOrg = await _db.Dashboards.AsNoTracking()
            .Join(_db.Workspaces, d => d.WorkspaceId, w => w.Id, (d, w) => new { d.Id, w.OrganizationId })
            .Where(x => orgIds.Contains(x.OrganizationId))
            .GroupBy(x => x.OrganizationId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToListAsync();
        var dashMap = dashboardsByOrg.ToDictionary(d => d.OrgId, d => d.Count);

        var datasourcesByOrg = await _db.Datasources.AsNoTracking()
            .Where(d => orgIds.Contains(d.OrganizationId))
            .GroupBy(d => d.OrganizationId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToListAsync();
        var dsMap = datasourcesByOrg.ToDictionary(d => d.OrgId, d => d.Count);

        var result = new List<OrgHealthItem>();
        foreach (var org in orgs)
        {
            var uids       = userMap.TryGetValue(org.Id, out var ids) ? ids : new List<string>();
            int totalUsers = uids.Count;
            int activeUsers = uids.Count(u => activeUserSet.Contains(u));
            int msgsInPeriod = uids.Sum(u => msgsByUser.TryGetValue(u, out var m) ? m : 0);
            int openTicketCount = ticketMap.TryGetValue(org.Id, out var tc) ? tc : 0;
            DateTime? lastPayment = paymentMap.TryGetValue(org.Id, out var lp) ? (DateTime?)lp : null;
            bool hasDashboard  = dashMap.TryGetValue(org.Id, out var dc) && dc > 0;
            bool hasDatasource = dsMap.TryGetValue(org.Id, out var dsc) && dsc > 0;

            // Activity (30 pts): msgs/active-user/day in last 7 days
            double msgsPerUserPerDay = activeUsers > 0 ? (double)msgsInPeriod / activeUsers / 7 : 0;
            double activityScore  = Math.Min(30, msgsPerUserPerDay >= 5 ? 30 : (msgsPerUserPerDay / 5.0) * 30);

            // Engagement (20 pts)
            double engagementScore = totalUsers > 0 ? Math.Min(20, (double)activeUsers / totalUsers * 20) : 0;

            // Support (15 pts)
            double supportScore = Math.Max(0, 15 - 3 * openTicketCount);

            // Payments (20 pts)
            double paymentScore = lastPayment.HasValue
                ? ((now - lastPayment.Value).TotalDays <= 30 ? 20
                 : (now - lastPayment.Value).TotalDays <= 60 ? 10
                 : 0)
                : 0;

            // Adoption (15 pts)
            double adoptionScore = (hasDashboard ? 7.5 : 0) + (hasDatasource ? 7.5 : 0);

            int total = (int)Math.Round(activityScore + engagementScore + supportScore + paymentScore + adoptionScore);

            result.Add(new OrgHealthItem
            {
                OrgId           = org.Id,
                OrgName         = org.Name,
                Plan            = org.Plan.ToString(),
                HealthScore     = total,
                ActivityScore   = (int)Math.Round(activityScore),
                EngagementScore = (int)Math.Round(engagementScore),
                SupportScore    = (int)Math.Round(supportScore),
                PaymentScore    = (int)Math.Round(paymentScore),
                AdoptionScore   = (int)Math.Round(adoptionScore),
                ActiveUsers     = activeUsers,
                TotalUsers      = totalUsers,
                OpenTickets     = openTicketCount,
                LastPaymentAt   = lastPayment,
                Tips            = BuildTips(activityScore, engagementScore, supportScore, paymentScore, adoptionScore, lastPayment, now)
            });
        }

        return result.OrderByDescending(o => o.HealthScore).ToList();
    }

    private static List<string> BuildTips(double activity, double engagement, double support, double payment,
        double adoption, DateTime? lastPayment, DateTime now)
    {
        var tips = new List<string>();
        if (activity < 15)
            tips.Add("Low activity — fewer than 5 messages per active user per day in the last week.");
        if (engagement < 10)
            tips.Add("Low engagement — fewer than half the users were active this week.");
        if (support < 9)
            tips.Add("Multiple open support tickets — prioritise resolution.");
        if (payment == 0)
        {
            tips.Add(lastPayment.HasValue
                ? $"No payments in 60+ days — consider follow-up (last: {lastPayment.Value:MMM d, yyyy})."
                : "No payments on record.");
        }
        if (adoption < 10)
            tips.Add("Low adoption — encourage users to create dashboards and connect datasources.");
        if (tips.Count == 0)
            tips.Add("Organisation is performing well across all dimensions.");
        return tips;
    }

    // ──────────────────────────────────────────────────────────
    // View-model classes
    // ──────────────────────────────────────────────────────────

    public class UserSearchItem
    {
        public string Id           { get; set; } = "";
        public string Email        { get; set; } = "";
        public string FullName     { get; set; } = "";
        public string Role         { get; set; } = "";
        public int? OrganizationId { get; set; }
        public string? OrgName     { get; set; }
        public DateTime CreatedAt  { get; set; }
    }

    public class UserTimelineViewModel
    {
        public ApplicationUser User            { get; set; } = null!;
        public string OrgName                  { get; set; } = "";
        public int? OrgId                      { get; set; }
        public string Plan                     { get; set; } = "Free";
        public int TotalActionsAllTime         { get; set; }
        public int TotalActionsInWindow        { get; set; }
        public DateTime? LastSeen              { get; set; }
        public int TotalMessages               { get; set; }
        public long TotalTokens               { get; set; }
        public List<ActivityLog> Logs          { get; set; } = new();
        public int Page                        { get; set; }
        public int PageSize                    { get; set; }
        public DateTime From                   { get; set; }
        public DateTime To                     { get; set; }
    }

    public class ChurnRiskItem
    {
        public int OrgId               { get; set; }
        public string OrgName          { get; set; } = "";
        public string Plan             { get; set; } = "";
        public DateTime? LastActivityAt { get; set; }
        public int DaysSinceActivity   { get; set; }
        public DateTime? LastPaymentAt { get; set; }
        public decimal MrrEstimate     { get; set; }
        public int OpenSupportTickets  { get; set; }
        public int RiskScore           { get; set; }
    }

    public class OrgHealthItem
    {
        public int OrgId               { get; set; }
        public string OrgName          { get; set; } = "";
        public string Plan             { get; set; } = "";
        public int HealthScore         { get; set; }
        public int ActivityScore       { get; set; }
        public int EngagementScore     { get; set; }
        public int SupportScore        { get; set; }
        public int PaymentScore        { get; set; }
        public int AdoptionScore       { get; set; }
        public int ActiveUsers         { get; set; }
        public int TotalUsers          { get; set; }
        public int OpenTickets         { get; set; }
        public DateTime? LastPaymentAt { get; set; }
        public List<string> Tips       { get; set; } = new();
    }
}
