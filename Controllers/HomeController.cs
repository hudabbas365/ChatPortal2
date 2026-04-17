using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Text;

namespace AIInsights.Controllers;

public class HomeController : Controller
{
    private readonly ISeoService _seoService;
    private readonly AppDbContext _db;

    private static readonly JsonSerializerSettings CamelCaseSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };

    public HomeController(ISeoService seoService, AppDbContext db, IConfiguration config)
    {
        _seoService = seoService;
        _db = db;
        _config = config;
    }

    private readonly IConfiguration _config;

    private async Task SetSeoAsync(string path)
    {
        ViewBag.SeoEntry = await _seoService.GetByUrlAsync(path);
    }

    public async Task<IActionResult> Index()
    {
        await SetSeoAsync("/");
        var comingSoon = _config.GetValue<bool>("App:ComingSoon");
        if (comingSoon)
            return View("Index");
        return View("Index.Original");
    }

    [Route("/about")]
    public async Task<IActionResult> About()
    {
        await SetSeoAsync("/about");
        return View();
    }

    public async Task<IActionResult> Pricing()
    {
        await SetSeoAsync("/pricing");
        return View();
    }

    [Route("/docs")]
    public async Task<IActionResult> Docs()
    {
        await SetSeoAsync("/docs");
        var articles = await _db.DocArticles
            .AsNoTracking()
            .Where(d => d.IsPublished)
            .OrderBy(d => d.SortOrder)
            .ThenByDescending(d => d.UpdatedAt)
            .Select(d => new DocArticle
            {
                Id = d.Id,
                Title = d.Title,
                Slug = d.Slug,
                Summary = d.Summary,
                SortOrder = d.SortOrder,
                UpdatedAt = d.UpdatedAt
            })
            .ToListAsync();
        return View(articles);
    }

    [Route("/blog")]
    public async Task<IActionResult> Blog()
    {
        await SetSeoAsync("/blog");
        var posts = await _db.BlogPosts
            .Where(p => p.IsPublished)
            .OrderByDescending(p => p.PublishedAt)
            .ToListAsync();
        return View(posts);
    }

    [Route("/blog/{slug}")]
    public async Task<IActionResult> BlogPost(string slug)
    {
        var post = await _db.BlogPosts
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished);
        if (post == null) return NotFound();
        await SetSeoAsync($"/blog/{slug}");
        return View(post);
    }

    [Route("/docs/{slug}")]
    public async Task<IActionResult> DocArticle(string slug)
    {
        var article = await _db.DocArticles
            .FirstOrDefaultAsync(d => d.Slug == slug && d.IsPublished);
        if (article == null) return NotFound();
        await SetSeoAsync($"/docs/{slug}");
        return View(article);
    }

    [Route("/sitemap.xml")]
    [ResponseCache(Duration = 3600)]
    public IActionResult Sitemap()
    {
        var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? "https://aiinsights.io";
        var urls = new[]
        {
            new { Loc = "/",       Priority = "1.0", ChangeFreq = "daily"   },
            new { Loc = "/about",  Priority = "0.8", ChangeFreq = "monthly" },
            new { Loc = "/docs",   Priority = "0.8", ChangeFreq = "weekly"  },
            new { Loc = "/blog",   Priority = "0.7", ChangeFreq = "weekly"  },
            new { Loc = "/pricing",Priority = "0.7", ChangeFreq = "monthly" },
        };

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var u in urls)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseUrl}{u.Loc}</loc>");
            sb.AppendLine($"    <changefreq>{u.ChangeFreq}</changefreq>");
            sb.AppendLine($"    <priority>{u.Priority}</priority>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }

    [Route("/robots.txt")]
    [ResponseCache(Duration = 3600)]
    public IActionResult Robots()
    {
        var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? "https://aiinsights.io";
        var content = $"User-agent: *\nAllow: /\nDisallow: /auth/\nDisallow: /chat/\nDisallow: /dashboard/\nDisallow: /superadmin/\n\nSitemap: {baseUrl}/sitemap.xml\n";
        return Content(content, "text/plain", Encoding.UTF8);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();

    [Route("/access-denied")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult AccessDenied([FromQuery] int? statusCode)
    {
        ViewData["StatusCode"] = statusCode ?? 401;
        return View();
    }
}
