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
    public async Task<IActionResult> Sitemap()
    {
        var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        var xml = await _seoService.GenerateSitemapXmlAsync(baseUrl);
        return Content(xml, "application/xml", Encoding.UTF8);
    }

    [Route("/robots.txt")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Robots()
    {
        var baseUrl = _config["App:BaseUrl"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        var txt = await _seoService.GenerateRobotsTxtAsync(baseUrl);
        return Content(txt, "text/plain", Encoding.UTF8);
    }

    [Route("/terms")]
    public async Task<IActionResult> Terms()
    {
        await SetSeoAsync("/terms");
        return View();
    }

    [Route("/home/sla")]
    [Route("/sla")]
    public async Task<IActionResult> Sla()
    {
        await SetSeoAsync("/sla");
        return View();
    }

    [Route("/home/support")]
    [Route("/support")]
    public async Task<IActionResult> Support()
    {
        await SetSeoAsync("/support");
        return View();
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
