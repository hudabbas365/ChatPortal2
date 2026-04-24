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
public class SuperAdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly CohereService _cohere;
    private readonly IServiceScopeFactory _scopeFactory;

    public SuperAdminController(AppDbContext db, CohereService cohere, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _cohere = cohere;
        _scopeFactory = scopeFactory;
    }

    private string? GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    // Verifies SuperAdmin role both from JWT claims AND database for defense-in-depth
    private async Task<bool> IsSuperAdminAsync()
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
        ViewBag.ProUsers = stats.ProUsers;
        ViewBag.EnterpriseUsers = stats.EnterpriseUsers;
        ViewBag.TotalIncome = stats.TotalIncome;
        ViewBag.ActiveTrials = stats.ActiveTrials;

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

        var plans = await _db.SubscriptionPlans.ToListAsync();
        var proCount = plans.Count(p => p.Plan == PlanType.Professional);
        var enterpriseCount = plans.Count(p => p.Plan == PlanType.Enterprise);

        return new DashboardStatsDto
        {
            TotalOrgs = totalOrgs,
            TotalUsers = totalUsers,
            TotalWorkspaces = totalWorkspaces,
            TotalMessages = totalMessages,
            ProUsers = proCount,
            EnterpriseUsers = enterpriseCount,
            TotalIncome = proCount * PlanPricing.ProPricePerUser + enterpriseCount * PlanPricing.EnterprisePricePerUser,
            ActiveTrials = plans.Count(p => p.IsTrialActive)
        };
    }

    public class DashboardStatsDto
    {
        public int TotalOrgs { get; set; }
        public int TotalUsers { get; set; }
        public int TotalWorkspaces { get; set; }
        public int TotalMessages { get; set; }
        public int ProUsers { get; set; }
        public int EnterpriseUsers { get; set; }
        public decimal TotalIncome { get; set; }
        public int ActiveTrials { get; set; }
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

    // ── Notifications ─────────────────────────────────────────────────────────
    [HttpGet("/superadmin/notifications")]
    public async Task<IActionResult> Notifications()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var items = await _db.Notifications
            .OrderByDescending(n => n.CreatedAt)
            .Take(200)
            .ToListAsync();
        var orgs = await _db.Organizations
            .OrderBy(o => o.Name)
            .Select(o => new { o.Id, o.Name })
            .ToListAsync();
        var templates = await _db.NotificationTemplates
            .OrderBy(t => t.Name)
            .ToListAsync();
        ViewBag.Orgs = orgs;
        ViewBag.Templates = templates;
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
        var now = DateTime.UtcNow;
        var severity = string.IsNullOrWhiteSpace(dto.Severity) ? "normal" : dto.Severity!.Trim();
        var isScheduled = dto.ScheduleAt.HasValue && dto.ScheduleAt.Value > now;

        // Validate scope-specific parameters
        if (scope.Equals("SpecificOrgs", StringComparison.OrdinalIgnoreCase) || scope.Equals("Org", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.OrganizationIds == null || dto.OrganizationIds.Count == 0)
                return BadRequest(new { error = "At least one organization must be selected." });
        }
        else if (scope.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.UserIds == null || dto.UserIds.Count == 0)
                return BadRequest(new { error = "At least one user ID must be provided." });
        }
        else if (scope.Equals("Role", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.Roles == null || dto.Roles.Count == 0)
                return BadRequest(new { error = "At least one role must be provided." });
        }

        var notifications = new List<Notification>();

        if (scope.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            notifications.Add(new Notification
            {
                Scope = "All",
                Title = dto.Title!.Trim(),
                Body = dto.Body!.Trim(),
                Type = string.IsNullOrWhiteSpace(dto.Type) ? "Announcement" : dto.Type!.Trim(),
                Severity = severity,
                Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link!.Trim(),
                ExpiresAt = dto.ExpiresAt,
                CreatedByUserId = callerId,
                CreatedByRole = "SuperAdmin",
                CreatedAt = now,
                ScheduleAt = dto.ScheduleAt,
                DeliveryStatus = isScheduled ? "Scheduled" : "Delivered",
                DeliveredAt = isScheduled ? null : now
            });
        }
        else if (scope.Equals("SpecificOrgs", StringComparison.OrdinalIgnoreCase) || scope.Equals("Org", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var orgId in dto.OrganizationIds!.Distinct())
            {
                notifications.Add(new Notification
                {
                    Scope = "Org",
                    OrganizationId = orgId,
                    Title = dto.Title!.Trim(),
                    Body = dto.Body!.Trim(),
                    Type = string.IsNullOrWhiteSpace(dto.Type) ? "Announcement" : dto.Type!.Trim(),
                    Severity = severity,
                    Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link!.Trim(),
                    ExpiresAt = dto.ExpiresAt,
                    CreatedByUserId = callerId,
                    CreatedByRole = "SuperAdmin",
                    CreatedAt = now,
                    ScheduleAt = dto.ScheduleAt,
                    DeliveryStatus = isScheduled ? "Scheduled" : "Delivered",
                    DeliveredAt = isScheduled ? null : now
                });
            }
        }
        else if (scope.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            notifications.Add(new Notification
            {
                Scope = "User",
                TargetUserIdsCsv = string.Join(",", dto.UserIds!.Select(id => id.Trim()).Distinct()),
                Title = dto.Title!.Trim(),
                Body = dto.Body!.Trim(),
                Type = string.IsNullOrWhiteSpace(dto.Type) ? "Announcement" : dto.Type!.Trim(),
                Severity = severity,
                Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link!.Trim(),
                ExpiresAt = dto.ExpiresAt,
                CreatedByUserId = callerId,
                CreatedByRole = "SuperAdmin",
                CreatedAt = now,
                ScheduleAt = dto.ScheduleAt,
                DeliveryStatus = isScheduled ? "Scheduled" : "Delivered",
                DeliveredAt = isScheduled ? null : now
            });
        }
        else if (scope.Equals("Role", StringComparison.OrdinalIgnoreCase))
        {
            notifications.Add(new Notification
            {
                Scope = "Role",
                TargetRolesCsv = string.Join(",", dto.Roles!.Select(r => r.Trim()).Distinct()),
                Title = dto.Title!.Trim(),
                Body = dto.Body!.Trim(),
                Type = string.IsNullOrWhiteSpace(dto.Type) ? "Announcement" : dto.Type!.Trim(),
                Severity = severity,
                Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link!.Trim(),
                ExpiresAt = dto.ExpiresAt,
                CreatedByUserId = callerId,
                CreatedByRole = "SuperAdmin",
                CreatedAt = now,
                ScheduleAt = dto.ScheduleAt,
                DeliveryStatus = isScheduled ? "Scheduled" : "Delivered",
                DeliveredAt = isScheduled ? null : now
            });
        }
        else
        {
            return BadRequest(new { error = $"Unknown scope '{scope}'." });
        }

        _db.Notifications.AddRange(notifications);
        await _db.SaveChangesAsync();

        // Log activity
        foreach (var n in notifications)
        {
            _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
            {
                Action = "Notification.Broadcast",
                Description = $"Broadcast '{n.Title}' scope={n.Scope} status={n.DeliveryStatus}",
                UserId = callerId ?? "",
                CreatedAt = now
            });
        }
        await _db.SaveChangesAsync();

        // Fan out UserNotification rows immediately if not scheduled
        if (!isScheduled)
        {
            var emailer = HttpContext.RequestServices.GetService<AIInsights.SuperAdmin.Services.IUrgentNotificationEmailer>();
            var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            foreach (var n in notifications)
            {
                await FanOutAsync(n, emailer, config);
            }
        }

        return Ok(new { success = true, count = notifications.Count, scheduled = isScheduled });
    }

    /// <summary>Resolve recipients and create UserNotification rows (for User/Role scoped notifications).</summary>
    private async Task FanOutAsync(Notification notification,
        AIInsights.SuperAdmin.Services.IUrgentNotificationEmailer? emailer,
        IConfiguration config)
    {
        if (notification.Scope != "User" && notification.Scope != "Role")
            return; // All and Org use implicit fan-out via BaseQueryFor in NotificationsController

        var recipients = await AIInsights.SuperAdmin.Services.NotificationDispatcher
            .ResolveRecipientsAsync(_db, notification, CancellationToken.None);

        var distinctIds = recipients.Select(r => r.Id).Distinct().ToHashSet();
        var isUrgent = string.Equals(notification.Severity, "urgent", StringComparison.OrdinalIgnoreCase);
        var baseUrl = config["AppBaseUrl"] ?? "";

        var newRows = new List<AIInsights.Models.UserNotification>();
        foreach (var uid in distinctIds)
        {
            newRows.Add(new AIInsights.Models.UserNotification
            {
                UserId = uid,
                NotificationId = notification.Id
            });
        }

        if (newRows.Count > 0)
        {
            _db.UserNotifications.AddRange(newRows);
            await _db.SaveChangesAsync();

            if (isUrgent && emailer != null)
            {
                var recipientMap = recipients.ToDictionary(r => r.Id);
                var rowData = newRows.Select(r => new { r.Id, r.UserId }).ToList();
                var notificationId = notification.Id;
                var notificationTitle = notification.Title;
                var notificationBody = notification.Body;
                var scopeFactory = _scopeFactory;

                _ = Task.Run(async () =>
                {
                    foreach (var rd in rowData)
                    {
                        var user = recipientMap.TryGetValue(rd.UserId, out var u) ? u : null;
                        var email = user?.Email ?? "";
                        var name = user?.FullName ?? "";
                        if (string.IsNullOrWhiteSpace(email)) continue;

                        var clickUrl = $"{baseUrl}/n/{rd.Id}/click";
                        try
                        {
                            var sent = await emailer.SendAsync(email, name,
                                notificationTitle, notificationBody, clickUrl, CancellationToken.None);
                            if (sent)
                            {
                                using var scope = scopeFactory.CreateScope();
                                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                var un = await scopedDb.UserNotifications.FindAsync(rd.Id);
                                if (un != null)
                                {
                                    un.EmailSent = true;
                                    await scopedDb.SaveChangesAsync(CancellationToken.None);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            var logger = scopeFactory.CreateScope().ServiceProvider
                                .GetRequiredService<ILogger<SuperAdminController>>();
                            logger.LogError(ex, "Failed to send urgent email for UserNotification {Id}.", rd.Id);
                        }
                    }
                }, CancellationToken.None);
            }
        }
    }

    [HttpPost("/api/superadmin/notifications/{id:int}/cancel")]
    public async Task<IActionResult> CancelNotification(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();
        if (n.DeliveryStatus != "Scheduled")
            return BadRequest(new { error = "Only scheduled notifications can be cancelled." });

        n.DeliveryStatus = "Cancelled";
        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Cancel",
            Description = $"Cancelled scheduled notification '{n.Title}' (id={n.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPut("/api/superadmin/notifications/{id:int}")]
    public async Task<IActionResult> EditNotification(int id, [FromBody] EditNotificationDto dto)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (dto == null) return BadRequest(new { error = "Body required." });

        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();
        if (n.DeliveryStatus != "Scheduled")
            return BadRequest(new { error = "Only scheduled notifications can be edited." });

        if (!string.IsNullOrWhiteSpace(dto.Title)) n.Title = dto.Title.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Body)) n.Body = dto.Body.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Type)) n.Type = dto.Type.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Severity)) n.Severity = dto.Severity.Trim();
        if (dto.Link != null) n.Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link.Trim();
        if (dto.ExpiresAt.HasValue) n.ExpiresAt = dto.ExpiresAt;
        if (dto.ScheduleAt.HasValue) n.ScheduleAt = dto.ScheduleAt;
        if (!string.IsNullOrWhiteSpace(dto.Scope)) n.Scope = dto.Scope.Trim();
        if (dto.TargetUserIds != null) n.TargetUserIdsCsv = string.Join(",", dto.TargetUserIds);
        if (dto.TargetRoles != null) n.TargetRolesCsv = string.Join(",", dto.TargetRoles);

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Edit",
            Description = $"Edited scheduled notification '{n.Title}' (id={n.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/superadmin/notifications/{id:int}/recall")]
    public async Task<IActionResult> RecallNotification(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();
        if (n.IsRecalled)
            return BadRequest(new { error = "Notification is already recalled." });

        var now = DateTime.UtcNow;
        n.IsRecalled = true;
        n.RecalledAt = now;
        n.RecalledByUserId = GetCurrentUserId();

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Recall",
            Description = $"Recalled notification '{n.Title}' (id={n.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = now
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("/api/superadmin/notifications/{id:int}/metrics")]
    public async Task<IActionResult> NotificationMetrics(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();

        var rows = await _db.UserNotifications
            .Where(un => un.NotificationId == id)
            .Select(un => new { un.ReadAt, un.IsClicked, un.EmailSent })
            .ToListAsync();

        var total = rows.Count;
        var read = rows.Count(r => r.ReadAt != null);
        var clicked = rows.Count(r => r.IsClicked);
        var emailed = rows.Count(r => r.EmailSent);

        return Ok(new
        {
            id = n.Id,
            title = n.Title,
            deliveryStatus = n.DeliveryStatus,
            scheduledAt = n.ScheduleAt,
            deliveredAt = n.DeliveredAt,
            totalRecipients = total,
            read,
            unread = total - read,
            clicked,
            emailed,
            readRate = total > 0 ? Math.Round((double)read / total, 4) : 0.0,
            clickRate = total > 0 ? Math.Round((double)clicked / total, 4) : 0.0
        });
    }

    [HttpDelete("/api/superadmin/notifications/{id:int}")]
    public async Task<IActionResult> DeleteNotification(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();
        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Delete",
            Description = $"Deleted notification '{n.Title}' (id={n.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        _db.Notifications.Remove(n);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ── Notification Templates ────────────────────────────────────────────────

    [HttpGet("/api/superadmin/notification-templates")]
    public async Task<IActionResult> GetTemplates()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var list = await _db.NotificationTemplates.OrderBy(t => t.Name).ToListAsync();
        return Ok(list);
    }

    [HttpPost("/api/superadmin/notification-templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] NotificationTemplateDto dto)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { error = "Name is required." });

        var now = DateTime.UtcNow;
        var tmpl = new AIInsights.Models.NotificationTemplate
        {
            Name = dto.Name.Trim(),
            Title = dto.Title?.Trim() ?? "",
            Body = dto.Body?.Trim() ?? "",
            Type = dto.Type?.Trim() ?? "Announcement",
            Severity = dto.Severity?.Trim() ?? "normal",
            Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link.Trim(),
            CreatedByUserId = GetCurrentUserId(),
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.NotificationTemplates.Add(tmpl);
        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Template.Create",
            Description = $"Created notification template '{tmpl.Name}'",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = now
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true, id = tmpl.Id });
    }

    [HttpPut("/api/superadmin/notification-templates/{id:int}")]
    public async Task<IActionResult> UpdateTemplate(int id, [FromBody] NotificationTemplateDto dto)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (dto == null) return BadRequest(new { error = "Body required." });

        var tmpl = await _db.NotificationTemplates.FindAsync(id);
        if (tmpl == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Name)) tmpl.Name = dto.Name.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Title)) tmpl.Title = dto.Title.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Body)) tmpl.Body = dto.Body.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Type)) tmpl.Type = dto.Type.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Severity)) tmpl.Severity = dto.Severity.Trim();
        if (dto.Link != null) tmpl.Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link.Trim();
        tmpl.UpdatedAt = DateTime.UtcNow;

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Template.Update",
            Description = $"Updated notification template '{tmpl.Name}' (id={tmpl.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/superadmin/notification-templates/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var tmpl = await _db.NotificationTemplates.FindAsync(id);
        if (tmpl == null) return NotFound();

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Template.Delete",
            Description = $"Deleted notification template '{tmpl.Name}' (id={tmpl.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        _db.NotificationTemplates.Remove(tmpl);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // DTOs
    public class BroadcastNotificationDto
    {
        public string? Scope { get; set; } // "All" | "Org" | "SpecificOrgs" | "User" | "Role"
        public List<int>? OrganizationIds { get; set; }
        public List<string>? UserIds { get; set; }
        public List<string>? Roles { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Type { get; set; }
        public string? Severity { get; set; }
        public string? Link { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? ScheduleAt { get; set; }
    }

    public class EditNotificationDto
    {
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Type { get; set; }
        public string? Severity { get; set; }
        public string? Link { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? ScheduleAt { get; set; }
        public string? Scope { get; set; }
        public List<string>? TargetUserIds { get; set; }
        public List<string>? TargetRoles { get; set; }
    }

    public class NotificationTemplateDto
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Type { get; set; }
        public string? Severity { get; set; }
        public string? Link { get; set; }
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

        org.IsBlocked = true;
        org.BlockedReason = req.Reason;
        org.BlockedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/superadmin/orgs/{id}/unblock")]
    public async Task<IActionResult> UnblockOrg(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        org.IsBlocked = false;
        org.BlockedReason = null;
        org.BlockedAt = null;
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    public class BlockOrgRequest
    {
        public string? Reason { get; set; }
    }
}
