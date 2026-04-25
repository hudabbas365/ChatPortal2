using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace AIInsights.SuperAdmin.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public class SuperAdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly CohereService _cohere;

    public SuperAdminController(AppDbContext db, CohereService cohere)
    {
        _db = db;
        _cohere = cohere;
    }

    protected string? GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    // Verifies SuperAdmin role both from JWT claims AND database for defense-in-depth
    protected async Task<bool> IsSuperAdminAsync()
    {
        if (!User.Claims.Any(c => c.Type == "role" && c.Value == "SuperAdmin"))
            return false;
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId)) return false;
        var user = await _db.Users.FindAsync(userId) as ApplicationUser;
        return user?.Role == "SuperAdmin";
    }

    [HttpGet("/superadmin")]
    public async Task<IActionResult> Index()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var stats = await GetDashboardStatsAsync();
        ViewBag.TotalOrgs = stats.TotalOrgs;
        ViewBag.TotalUsers = stats.TotalUsers;
        ViewBag.TotalWorkspaces = stats.TotalWorkspaces;
        ViewBag.TotalMessages = stats.TotalMessages;
        ViewBag.ProUsers = stats.ProSubscriptions;
        ViewBag.EnterpriseUsers = stats.EnterpriseSubscriptions;
        ViewBag.TotalIncome = stats.TotalIncome;
        ViewBag.ActiveTrials = stats.ActiveTrials;
        ViewBag.ActiveNow = stats.ActiveNow;
        ViewBag.ActiveToday = stats.ActiveToday;
        ViewBag.Dau = stats.Dau;
        ViewBag.Wau = stats.Wau;
        ViewBag.Mau = stats.Mau;

        return View("~/Views/Admin/Index.cshtml");
    }

    [HttpGet("/api/superadmin/dashboard-stats")]
    public async Task<IActionResult> DashboardStats()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var stats = await GetDashboardStatsAsync();
        return Ok(stats);
    }

    private async Task<DashboardStatsDto> GetDashboardStatsAsync()
    {
        var totalOrgs = await _db.Organizations.CountAsync();
        var totalUsers = await _db.Users.CountAsync();
        var totalWorkspaces = await _db.Workspaces.CountAsync();
        var totalMessages = await _db.ChatMessages.CountAsync();

        var proCount = await _db.SubscriptionPlans.CountAsync(p => p.Plan == PlanType.Professional);
        var enterpriseCount = await _db.SubscriptionPlans.CountAsync(p => p.Plan == PlanType.Enterprise);
        var activeTrials = await _db.SubscriptionPlans.CountAsync(p => p.IsTrialActive);

        var now = DateTime.UtcNow;
        var activeNow = await _db.Users.CountAsync(u => u.LastSeenAt != null && u.LastSeenAt >= now.AddMinutes(-5));
        var activeToday = await _db.Users.CountAsync(u => u.LastSeenAt != null && u.LastSeenAt >= now.Date);

        // Single pass over ActivityLogs: compute DAU/WAU/MAU thresholds and count distinct users per band
        var mauCutoff = now.AddDays(-30);
        var wauCutoff = now.AddDays(-7);
        var dauCutoff = now.AddDays(-1);

        var activityCounts = await _db.ActivityLogs
            .Where(l => l.CreatedAt >= mauCutoff)
            .GroupBy(l => l.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                MaxDate = g.Max(l => l.CreatedAt)
            })
            .ToListAsync();

        var mau = activityCounts.Count;
        var wau = activityCounts.Count(g => g.MaxDate >= wauCutoff);
        var dau = activityCounts.Count(g => g.MaxDate >= dauCutoff);

        return new DashboardStatsDto
        {
            TotalOrgs = totalOrgs,
            TotalUsers = totalUsers,
            TotalWorkspaces = totalWorkspaces,
            TotalMessages = totalMessages,
            ProSubscriptions = proCount,
            EnterpriseSubscriptions = enterpriseCount,
            TotalIncome = proCount * PlanPricing.ProPricePerUser + enterpriseCount * PlanPricing.EnterprisePricePerUser,
            ActiveTrials = activeTrials,
            ActiveNow = activeNow,
            ActiveToday = activeToday,
            Dau = dau,
            Wau = wau,
            Mau = mau
        };
    }

    public class DashboardStatsDto
    {
        public int TotalOrgs { get; set; }
        public int TotalUsers { get; set; }
        public int TotalWorkspaces { get; set; }
        public int TotalMessages { get; set; }
        // Kept for JSON backward-compat; also exposed via new names below
        [Newtonsoft.Json.JsonProperty("proUsers")]
        public int ProSubscriptions { get; set; }
        [Newtonsoft.Json.JsonProperty("enterpriseUsers")]
        public int EnterpriseSubscriptions { get; set; }
        public decimal TotalIncome { get; set; }
        public int ActiveTrials { get; set; }
        public int ActiveNow { get; set; }
        public int ActiveToday { get; set; }
        public int Dau { get; set; }
        public int Wau { get; set; }
        public int Mau { get; set; }
    }

    [HttpGet("/superadmin/organizations")]
    public async Task<IActionResult> Organizations()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        // Use split query to avoid cartesian explosion from multiple collection includes,
        // which in EF Core 8 can cause some parent rows to be dropped when duplicate
        // ordering-key values combine with the large cross-product.
        var orgs = await _db.Organizations
            .Include(o => o.Users)
                .ThenInclude(u => u.Subscription)
            .Include(o => o.Workspaces)
            .AsSplitQuery()
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .ToListAsync();
        return View("~/Views/Admin/Organizations.cshtml", orgs);
    }

    [HttpGet("/api/admin/super/orgs")]
    public async Task<IActionResult> GetOrganizationsForPlanEditor()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var orgs = await _db.Organizations
            .OrderBy(o => o.Name)
            .Select(o => new { o.Id, o.Name, plan = o.Plan.ToString() })
            .ToListAsync();
        return Ok(orgs);
    }

    [HttpPut("/api/admin/super/orgs/{id}/plan")]
    public async Task<IActionResult> UpdateOrgPlan(int id, [FromBody] UpdateOrgPlanRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();
        if (!Enum.TryParse<PlanType>(req.Plan, true, out var plan))
            return BadRequest(new { error = "Invalid plan. Use: Free, FreeTrial, Professional, Enterprise" });

        var callerId = GetCurrentUserId();
        var callerEmail = User.FindFirstValue(System.Security.Claims.ClaimTypes.Email)
                          ?? User.FindFirstValue("email");

        var fromPlan = org.Plan;
        var fromLicenses = org.PurchasedLicenses;

        org.Plan = plan;

        // SuperAdmin grants a number of paid licenses to the organization.
        // OrgAdmin will assign these licenses to individual users.
        if (req.PurchasedLicenses.HasValue)
        {
            if (req.PurchasedLicenses.Value < 0)
                return BadRequest(new { error = "PurchasedLicenses must be zero or greater." });

            // Never let PurchasedLicenses drop below the number already assigned to users.
            var assignedCount = await _db.SubscriptionPlans
                .CountAsync(s => s.User!.OrganizationId == id
                                 && (s.Plan == PlanType.Professional || s.Plan == PlanType.Enterprise));

            if (req.PurchasedLicenses.Value < assignedCount)
                return BadRequest(new { error = $"Cannot reduce licenses below the {assignedCount} already assigned. Revoke user licenses first." });

            // Free / FreeTrial plans don't carry paid licenses.
            org.PurchasedLicenses = (plan == PlanType.Professional || plan == PlanType.Enterprise)
                ? req.PurchasedLicenses.Value
                : 0;
        }
        else if (plan != PlanType.Professional && plan != PlanType.Enterprise)
        {
            // Downgrading to a non-paid plan clears the license pool.
            org.PurchasedLicenses = 0;
        }

        var now = DateTime.UtcNow;
        _db.PlanChangeLogs.Add(new PlanChangeLog
        {
            OrganizationId = id,
            FromPlan = fromPlan.ToString(),
            ToPlan = org.Plan.ToString(),
            FromPurchasedLicenses = fromLicenses,
            ToPurchasedLicenses = org.PurchasedLicenses,
            FromLicenseEndsAt = org.LicenseEndsAt,
            ToLicenseEndsAt = org.LicenseEndsAt,
            ChangeType = "PlanChange",
            ChangedByUserId = callerId,
            ChangedByEmail = callerEmail,
            CreatedAt = now
        });

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Org.PlanChange",
            Description = $"Plan changed for {org.Name}: {fromPlan} → {org.Plan}, licenses {fromLicenses} → {org.PurchasedLicenses}",
            UserId = callerId ?? "",
            OrganizationId = id,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();
        return Ok(new
        {
            success = true,
            orgId = id,
            plan = org.Plan.ToString(),
            purchasedLicenses = org.PurchasedLicenses
        });
    }

    [HttpGet("/superadmin/activity")]
    public async Task<IActionResult> ActivityLogs([FromQuery] int page = 1, [FromQuery] string? search = null)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        const int pageSize = 50;

        var query = from log in _db.ActivityLogs
                    join user in _db.Users on log.UserId equals user.Id into uj
                    from user in uj.DefaultIfEmpty()
                    join org in _db.Organizations on log.OrganizationId equals org.Id into oj
                    from org in oj.DefaultIfEmpty()
                    select new ActivityLogViewModel
                    {
                        Id = log.Id,
                        Action = log.Action,
                        Description = log.Description,
                        CreatedAt = log.CreatedAt,
                        UserId = log.UserId,
                        UserName = user != null ? user.FullName : null,
                        UserEmail = user != null ? user.Email : null,
                        OrganizationId = log.OrganizationId,
                        OrganizationName = org != null ? org.Name : null
                    };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(l =>
                (l.UserName != null && l.UserName.ToLower().Contains(term)) ||
                (l.UserEmail != null && l.UserEmail.ToLower().Contains(term)) ||
                (l.OrganizationName != null && l.OrganizationName.ToLower().Contains(term)) ||
                l.Action.ToLower().Contains(term) ||
                l.Description.ToLower().Contains(term));
        }

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Page = page;
        ViewBag.Search = search;
        return View("~/Views/Admin/ActivityLogs.cshtml", logs);
    }

    public class ActivityLogViewModel
    {
        public int Id { get; set; }
        public string Action { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string UserId { get; set; } = "";
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
        public int? OrganizationId { get; set; }
        public string? OrganizationName { get; set; }
    }

    public class UpdateOrgPlanRequest
    {
        public string Plan { get; set; } = "";
        public int? PurchasedLicenses { get; set; }
    }

    [HttpGet("/superadmin/aiconfig")]
    public async Task<IActionResult> AiConfig()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        return View("~/Views/Admin/AiConfig.cshtml");
    }

    [HttpGet("/superadmin/revenue")]
    public async Task<IActionResult> Payments()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var plans = await _db.SubscriptionPlans
            .Include(p => p.User)
            .ToListAsync();

        var proCount = plans.Count(p => p.Plan == PlanType.Professional);
        var enterpriseCount = plans.Count(p => p.Plan == PlanType.Enterprise);
        ViewBag.ProCount = proCount;
        ViewBag.EnterpriseCount = enterpriseCount;
        ViewBag.ProRevenue = proCount * PlanPricing.ProPricePerUser;
        ViewBag.EnterpriseRevenue = enterpriseCount * PlanPricing.EnterprisePricePerUser;
        ViewBag.TotalIncome = proCount * PlanPricing.ProPricePerUser + enterpriseCount * PlanPricing.EnterprisePricePerUser;
        ViewBag.ActiveTrials = plans.Count(p => p.IsTrialActive);
        ViewBag.ExpiredTrials = plans.Count(p => p.IsTrialExpired);

        var paidUsers = await _db.Users
            .Where(u => u.CardLast4 != null)
            .Select(u => new { u.Id, u.FullName, u.Email, u.CardBrand, u.CardLast4 })
            .ToListAsync();
        ViewBag.PaidUsers = paidUsers;

        return View("~/Views/Admin/Revenue.cshtml");
    }

    [HttpGet("/superadmin/seo")]
    public async Task<IActionResult> Seo()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var entries = await _db.SeoEntries.OrderBy(s => s.PageUrl).ToListAsync();
        return View("~/Views/Admin/Seo.cshtml", entries);
    }

    [HttpPost("/superadmin/seo/save")]
    public async Task<IActionResult> SaveSeo([FromBody] SeoEntry entry)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        if (entry.Id > 0)
        {
            var existing = await _db.SeoEntries.FindAsync(entry.Id);
            if (existing == null) return NotFound();
            existing.Title = entry.Title;
            existing.MetaDescription = entry.MetaDescription;
            existing.MetaKeywords = entry.MetaKeywords;
            existing.OgTitle = entry.OgTitle;
            existing.OgDescription = entry.OgDescription;
            existing.RobotsDirective = entry.RobotsDirective;
            existing.LastModified = DateTime.UtcNow;
        }
        else
        {
            entry.LastModified = DateTime.UtcNow;
            entry.CreatedAt = DateTime.UtcNow;
            _db.SeoEntries.Add(entry);
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/superadmin/seo/autofill")]
    public async Task<IActionResult> AutoFillSeo()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var existing = await _db.SeoEntries.Select(s => s.PageUrl).ToListAsync();

        var defaults = new List<SeoEntry>
        {
            new() { PageUrl = "/", Title = "ChatPortal – AI-Powered Team Collaboration", MetaDescription = "Real-time AI chat, dashboards, and analytics for modern teams.", MetaKeywords = "chat, AI, collaboration, team, analytics", OgTitle = "ChatPortal – AI-Powered Team Collaboration", OgDescription = "Real-time AI chat, dashboards, and analytics for modern teams.", RobotsDirective = "index, follow" },
            new() { PageUrl = "/about", Title = "About – ChatPortal", MetaDescription = "Learn about ChatPortal's mission to empower teams with AI-driven collaboration.", MetaKeywords = "about, chatportal, team, mission", OgTitle = "About ChatPortal", OgDescription = "Our mission to empower teams with AI-driven collaboration.", RobotsDirective = "index, follow" },
            new() { PageUrl = "/pricing", Title = "Pricing – ChatPortal", MetaDescription = "Flexible pricing plans for teams of all sizes. Professional and Enterprise tiers available.", MetaKeywords = "pricing, plans, professional, enterprise", OgTitle = "ChatPortal Pricing", OgDescription = "Flexible pricing plans for teams of all sizes.", RobotsDirective = "index, follow" },
            new() { PageUrl = "/docs", Title = "Documentation – ChatPortal", MetaDescription = "Guides, API references, and tutorials to get started with ChatPortal.", MetaKeywords = "docs, documentation, API, guides, tutorials", OgTitle = "ChatPortal Documentation", OgDescription = "Guides, API references, and tutorials.", RobotsDirective = "index, follow" },
            new() { PageUrl = "/blog", Title = "Blog – ChatPortal", MetaDescription = "Latest news, tips, and product updates from the ChatPortal team.", MetaKeywords = "blog, news, updates, tips", OgTitle = "ChatPortal Blog", OgDescription = "Latest news, tips, and product updates.", RobotsDirective = "index, follow" },
            new() { PageUrl = "/auth/login", Title = "Sign In – ChatPortal", MetaDescription = "Sign in to your ChatPortal account to access your workspaces and conversations.", MetaKeywords = "login, sign in, account", OgTitle = "Sign In to ChatPortal", OgDescription = "Access your workspaces and conversations.", RobotsDirective = "noindex, follow" },
            new() { PageUrl = "/auth/register", Title = "Create Account – ChatPortal", MetaDescription = "Join ChatPortal and start collaborating with AI-powered chat and analytics.", MetaKeywords = "register, sign up, create account", OgTitle = "Create a ChatPortal Account", OgDescription = "Start collaborating with AI-powered chat and analytics.", RobotsDirective = "noindex, follow" },
            new() { PageUrl = "/dashboard", Title = "Dashboard – ChatPortal", MetaDescription = "Your analytics dashboard with charts, data sources, and workspace insights.", MetaKeywords = "dashboard, analytics, charts, insights", OgTitle = "ChatPortal Dashboard", OgDescription = "Analytics dashboard with charts and insights.", RobotsDirective = "noindex, nofollow" },
            new() { PageUrl = "/chat", Title = "Chat – ChatPortal", MetaDescription = "AI-powered chat workspace for real-time team collaboration.", MetaKeywords = "chat, AI, workspace, collaboration", OgTitle = "ChatPortal Chat", OgDescription = "AI-powered real-time team collaboration.", RobotsDirective = "noindex, nofollow" },
        };

        var toAdd = defaults.Where(d => !existing.Contains(d.PageUrl)).ToList();
        foreach (var entry in toAdd)
        {
            entry.CreatedAt = DateTime.UtcNow;
            entry.LastModified = DateTime.UtcNow;
        }

        _db.SeoEntries.AddRange(toAdd);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, added = toAdd.Count });
    }

    [HttpPost("/superadmin/seo/ai-suggest")]
    public async Task<IActionResult> AiSuggestSeo([FromBody] AiSuggestRequest request)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        if (string.IsNullOrWhiteSpace(request.PageUrl))
            return BadRequest(new { error = "Page URL is required." });

        var prompt = $@"You are an SEO expert. Generate optimized SEO metadata for a web page.
Page URL: {request.PageUrl}
Page context: {(string.IsNullOrWhiteSpace(request.Context) ? "AI-powered team collaboration and analytics platform called ChatPortal" : request.Context)}

Respond ONLY with valid JSON (no markdown, no code fences) in this exact format:
{{
  ""title"": ""Page title (50-60 chars)"",
  ""metaDescription"": ""Meta description (150-160 chars)"",
  ""metaKeywords"": ""comma, separated, keywords"",
  ""ogTitle"": ""Open Graph title"",
  ""ogDescription"": ""Open Graph description (under 200 chars)"",
  ""robotsDirective"": ""index, follow""
}}";

        var sb = new StringBuilder();
        await foreach (var chunk in _cohere.StreamChatAsync(prompt, [], "You are an SEO metadata generator. Return only valid JSON."))
        {
            sb.Append(chunk);
        }

        var raw = sb.ToString().Trim();
        // Strip markdown code fences if present
        if (raw.StartsWith("```"))
        {
            var firstNewline = raw.IndexOf('\n');
            var lastFence = raw.LastIndexOf("```");
            if (firstNewline > 0 && lastFence > firstNewline)
                raw = raw[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(raw);
            return Ok(result);
        }
        catch
        {
            return Ok(new { title = "", metaDescription = raw, metaKeywords = "", ogTitle = "", ogDescription = "", robotsDirective = "index, follow" });
        }
    }

    public class AiSuggestRequest
    {
        public string PageUrl { get; set; } = "";
        public string Context { get; set; } = "";
    }

    // ──── About ────
    [HttpGet("/superadmin/about")]
    public async Task<IActionResult> About()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        return View("~/Views/Admin/About.cshtml");
    }

    // ──── Documentation CRUD ────
    [HttpGet("/superadmin/docs")]
    public async Task<IActionResult> Docs()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var docs = await _db.DocArticles.OrderBy(d => d.SortOrder).ThenByDescending(d => d.CreatedAt).ToListAsync();
        return View("~/Views/Admin/Docs.cshtml", docs);
    }

    [HttpPost("/api/superadmin/docs/save")]
    public async Task<IActionResult> SaveDoc([FromBody] DocArticle doc)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        if (string.IsNullOrWhiteSpace(doc.Title))
            return BadRequest(new { error = "Title is required." });

        if (string.IsNullOrWhiteSpace(doc.Slug))
            doc.Slug = doc.Title.ToLower().Replace(" ", "-").Replace("--", "-");

        string? oldUrl = null;
        if (doc.Id > 0)
        {
            var existing = await _db.DocArticles.FindAsync(doc.Id);
            if (existing == null) return NotFound();
            oldUrl = $"/docs/{existing.Slug}";
            existing.Title = doc.Title;
            existing.Slug = doc.Slug;
            existing.Summary = doc.Summary;
            existing.Content = doc.Content;
            existing.Author = doc.Author;
            existing.SortOrder = doc.SortOrder;
            existing.IsPublished = doc.IsPublished;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            doc.CreatedAt = DateTime.UtcNow;
            doc.UpdatedAt = DateTime.UtcNow;
            _db.DocArticles.Add(doc);
        }

        await _db.SaveChangesAsync();
        await UpsertSeoForContentAsync(
            newUrl: $"/docs/{doc.Slug}",
            oldUrl: oldUrl,
            title: $"{doc.Title} — AIInsights365.net",
            description: doc.Summary ?? doc.Title,
            keywords: "AIInsights365, AI analytics, documentation, " + doc.Slug.Replace('-', ' '),
            priority: 0.7m,
            changeFreq: "monthly",
            includeInSitemap: doc.IsPublished);
        return Ok(new { success = true });
    }

    [HttpDelete("/api/superadmin/docs/{id}")]
    public async Task<IActionResult> DeleteDoc(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var doc = await _db.DocArticles.FindAsync(id);
        if (doc == null) return NotFound();
        var url = $"/docs/{doc.Slug}";
        _db.DocArticles.Remove(doc);
        await _db.SaveChangesAsync();
        await RemoveSeoByUrlAsync(url);
        return Ok(new { success = true });
    }

    // ──── Blog CRUD ────
    [HttpGet("/superadmin/blog")]
    public async Task<IActionResult> Blog()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var posts = await _db.BlogPosts.OrderByDescending(b => b.PublishedAt).ToListAsync();
        return View("~/Views/Admin/Blog.cshtml", posts);
    }

    [HttpPost("/api/superadmin/blog/save")]
    public async Task<IActionResult> SaveBlog([FromBody] BlogPost post)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        if (string.IsNullOrWhiteSpace(post.Title))
            return BadRequest(new { error = "Title is required." });

        if (string.IsNullOrWhiteSpace(post.Slug))
            post.Slug = post.Title.ToLower().Replace(" ", "-").Replace("--", "-");

        string? oldUrl = null;
        if (post.Id > 0)
        {
            var existing = await _db.BlogPosts.FindAsync(post.Id);
            if (existing == null) return NotFound();
            oldUrl = $"/blog/{existing.Slug}";
            existing.Title = post.Title;
            existing.Slug = post.Slug;
            existing.Summary = post.Summary;
            existing.Content = post.Content;
            existing.Author = post.Author;
            existing.ImageUrl = post.ImageUrl;
            if (!existing.IsPublished && post.IsPublished)
                existing.PublishedAt = DateTime.UtcNow;
            existing.IsPublished = post.IsPublished;
        }
        else
        {
            post.PublishedAt = DateTime.UtcNow;
            _db.BlogPosts.Add(post);
        }

        await _db.SaveChangesAsync();
        await UpsertSeoForContentAsync(
            newUrl: $"/blog/{post.Slug}",
            oldUrl: oldUrl,
            title: $"{post.Title} — AIInsights365.net",
            description: post.Summary ?? post.Title,
            keywords: "AIInsights365, blog, AI analytics, " + post.Slug.Replace('-', ' '),
            priority: 0.8m,
            changeFreq: "weekly",
            includeInSitemap: post.IsPublished);
        return Ok(new { success = true });
    }

    [HttpDelete("/api/superadmin/blog/{id}")]
    public async Task<IActionResult> DeleteBlog(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var post = await _db.BlogPosts.FindAsync(id);
        if (post == null) return NotFound();
        var url = $"/blog/{post.Slug}";
        _db.BlogPosts.Remove(post);
        await _db.SaveChangesAsync();
        await RemoveSeoByUrlAsync(url);
        return Ok(new { success = true });
    }

    // ── SEO helpers for content CRUD ──────────────────────────
    private async Task UpsertSeoForContentAsync(string newUrl, string? oldUrl, string title,
        string description, string keywords, decimal priority, string changeFreq, bool includeInSitemap)
    {
        // If the slug changed, drop the SEO row for the old URL so sitemap stays clean.
        if (!string.IsNullOrEmpty(oldUrl) && !string.Equals(oldUrl, newUrl, StringComparison.OrdinalIgnoreCase))
        {
            await RemoveSeoByUrlAsync(oldUrl);
        }

        var entry = await _db.SeoEntries.FirstOrDefaultAsync(s => s.PageUrl == newUrl);
        if (entry == null)
        {
            _db.SeoEntries.Add(new SeoEntry
            {
                PageUrl = newUrl,
                Title = title,
                MetaDescription = description,
                MetaKeywords = keywords,
                OgTitle = title,
                OgDescription = description,
                SitemapPriority = priority,
                SitemapChangeFreq = changeFreq,
                IncludeInSitemap = includeInSitemap,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            });
        }
        else
        {
            entry.Title = title;
            entry.MetaDescription = description;
            entry.MetaKeywords = keywords;
            entry.OgTitle = title;
            entry.OgDescription = description;
            entry.IncludeInSitemap = includeInSitemap;
            entry.LastModified = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    private async Task RemoveSeoByUrlAsync(string url)
    {
        var entry = await _db.SeoEntries.FirstOrDefaultAsync(s => s.PageUrl == url);
        if (entry != null)
        {
            _db.SeoEntries.Remove(entry);
            await _db.SaveChangesAsync();
        }
    }

    // ── Notifications: broadcast to all orgs OR specific orgs ────────────────
    [HttpGet("/superadmin/notifications")]
    public async Task<IActionResult> Notifications()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var items = await _db.Notifications
            .Where(n => n.Scope == "All" || n.Scope == "Org")
            .OrderByDescending(n => n.CreatedAt)
            .Take(200)
            .ToListAsync();
        var orgs = await _db.Organizations
            .OrderBy(o => o.Name)
            .Select(o => new { o.Id, o.Name })
            .ToListAsync();
        ViewBag.Orgs = orgs;
        return View("~/Views/Admin/Notifications.cshtml", items);
    }

    [HttpPost("/api/superadmin/notifications/broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] BroadcastNotificationDto dto)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (dto == null || string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Body))
            return BadRequest(new { error = "Title and body are required." });

        var callerId = GetCurrentUserId();
        var scope = string.IsNullOrWhiteSpace(dto.Scope) ? "All" : dto.Scope!.Trim();
        var created = new List<int>();
        var now = DateTime.UtcNow;

        Notification Build(int? orgId) => new()
        {
            Scope = orgId.HasValue ? "Org" : "All",
            OrganizationId = orgId,
            Title = dto.Title!.Trim(),
            Body = dto.Body!.Trim(),
            Type = string.IsNullOrWhiteSpace(dto.Type) ? "Announcement" : dto.Type!.Trim(),
            Severity = string.IsNullOrWhiteSpace(dto.Severity) ? "normal" : dto.Severity!.Trim(),
            Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link!.Trim(),
            ExpiresAt = dto.ExpiresAt,
            CreatedByUserId = callerId,
            CreatedByRole = "SuperAdmin",
            CreatedAt = now
        };

        int sentCount;
        if (scope.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var n = Build(null);
            _db.Notifications.Add(n);
            await _db.SaveChangesAsync();
            created.Add(n.Id);
            sentCount = 1;
        }
        else
        {
            if (dto.OrganizationIds == null || dto.OrganizationIds.Count == 0)
                return BadRequest(new { error = "At least one organization must be selected." });
            foreach (var orgId in dto.OrganizationIds.Distinct())
            {
                var n = Build(orgId);
                _db.Notifications.Add(n);
            }
            await _db.SaveChangesAsync();
            sentCount = dto.OrganizationIds.Count;
        }

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Notification.Broadcast",
            Description = $"Broadcast notification \"{dto.Title!.Trim()}\" (scope: {scope}, count: {sentCount})",
            UserId = callerId ?? "",
            CreatedAt = now
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true, count = sentCount });
    }

    [HttpDelete("/api/superadmin/notifications/{id:int}")]
    public async Task<IActionResult> DeleteNotification(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();

        var callerId = GetCurrentUserId();
        var title = n.Title;

        _db.Notifications.Remove(n);

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Notification.Delete",
            Description = $"Deleted notification \"{title}\" (id: {id})",
            UserId = callerId ?? "",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    public class BroadcastNotificationDto
    {
        public string? Scope { get; set; } // "All" | "SpecificOrgs"
        public List<int>? OrganizationIds { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Type { get; set; }
        public string? Severity { get; set; }
        public string? Link { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    // ──── Payments Tracking ────
    [HttpGet("/superadmin/payments")]
    public async Task<IActionResult> Payments_All([FromQuery] int page = 1, [FromQuery] string? search = null)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        const int pageSize = 50;

        var query = _db.PaymentRecords
            .Include(p => p.Organization)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                (p.Organization != null && p.Organization.Name.ToLower().Contains(term)) ||
                p.PaymentType.ToLower().Contains(term) ||
                p.Status.ToLower().Contains(term) ||
                (p.PayPalOrderId != null && p.PayPalOrderId.ToLower().Contains(term)) ||
                (p.Description != null && p.Description.ToLower().Contains(term)));
        }

        var records = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var totalCount = await query.CountAsync();

        ViewBag.Page = page;
        ViewBag.Search = search;
        ViewBag.TotalCount = totalCount;
        ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return View("~/Views/Admin/Payments.cshtml", records);
    }

    // ──── Block / Unblock Organizations ────
    [HttpGet("/superadmin/org-management")]
    public async Task<IActionResult> OrgManagement()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var orgs = await _db.Organizations
            .OrderByDescending(o => o.IsBlocked)
            .ThenBy(o => o.Name)
            .ToListAsync();

        return View("~/Views/Admin/OrgManagement.cshtml", orgs);
    }

    [HttpPost("/api/superadmin/orgs/{id}/block")]
    public async Task<IActionResult> BlockOrg(int id, [FromBody] BlockOrgRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        var callerId = GetCurrentUserId();
        var now = DateTime.UtcNow;

        org.IsBlocked = true;
        org.BlockedReason = req.Reason;
        org.BlockedAt = now;

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Org.Block",
            Description = $"Blocked organization {org.Name}. Reason: {req.Reason ?? "—"}",
            UserId = callerId ?? "",
            OrganizationId = id,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/superadmin/orgs/{id}/unblock")]
    public async Task<IActionResult> UnblockOrg(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        var callerId = GetCurrentUserId();
        var now = DateTime.UtcNow;

        org.IsBlocked = false;
        org.BlockedReason = null;
        org.BlockedAt = null;

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "Org.Unblock",
            Description = $"Unblocked organization {org.Name}.",
            UserId = callerId ?? "",
            OrganizationId = id,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    public class BlockOrgRequest
    {
        public string? Reason { get; set; }
    }

    [HttpGet("/superadmin/error")]
    [AllowAnonymous]
    public IActionResult Error()
    {
        return Content("An unexpected error occurred. Please try again later.", "text/plain");
    }
}
