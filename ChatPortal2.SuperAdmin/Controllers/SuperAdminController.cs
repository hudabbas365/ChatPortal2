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

    public SuperAdminController(AppDbContext db, CohereService cohere)
    {
        _db = db;
        _cohere = cohere;
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

        var orgs = await _db.Organizations
            .Include(o => o.Users)
                .ThenInclude(u => u.Subscription)
            .Include(o => o.Workspaces)
            .OrderByDescending(o => o.CreatedAt)
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
        await _db.SaveChangesAsync();
        return Ok(new { success = true, orgId = id, plan = org.Plan.ToString() });
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

        if (doc.Id > 0)
        {
            var existing = await _db.DocArticles.FindAsync(doc.Id);
            if (existing == null) return NotFound();
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
        return Ok(new { success = true });
    }

    [HttpDelete("/api/superadmin/docs/{id}")]
    public async Task<IActionResult> DeleteDoc(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var doc = await _db.DocArticles.FindAsync(id);
        if (doc == null) return NotFound();
        _db.DocArticles.Remove(doc);
        await _db.SaveChangesAsync();
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

        if (post.Id > 0)
        {
            var existing = await _db.BlogPosts.FindAsync(post.Id);
            if (existing == null) return NotFound();
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
        return Ok(new { success = true });
    }

    [HttpDelete("/api/superadmin/blog/{id}")]
    public async Task<IActionResult> DeleteBlog(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var post = await _db.BlogPosts.FindAsync(id);
        if (post == null) return NotFound();
        _db.BlogPosts.Remove(post);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}
