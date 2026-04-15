using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

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

    public HomeController(ISeoService seoService, AppDbContext db)
    {
        _seoService = seoService;
        _db = db;
    }

    private async Task SetSeoAsync(string path)
    {
        ViewBag.SeoEntry = await _seoService.GetByUrlAsync(path);
    }

    public async Task<IActionResult> Index()
    {
        await SetSeoAsync("/");
        return View();
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
