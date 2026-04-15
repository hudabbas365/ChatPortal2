using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AIInsights.Controllers;

public class SeoController : Controller
{
    private readonly ISeoService _seoService;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;

    public SeoController(ISeoService seoService, IConfiguration config, AppDbContext db)
    {
        _seoService = seoService;
        _config = config;
        _db = db;
    }

    private async Task<bool> IsAdminCallerAsync()
    {
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (string.IsNullOrEmpty(callerId)) return false;
        var caller = await _db.Users.FindAsync(callerId);
        return caller?.Role == "SuperAdmin" || caller?.Role == "OrgAdmin";
    }

    // PUBLIC ENDPOINTS

    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {
        var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var xml = await _seoService.GenerateSitemapXmlAsync(baseUrl);
        return Content(xml, "application/xml");
    }

    [HttpGet("/robots.txt")]
    public async Task<IActionResult> RobotsTxt()
    {
        var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var txt = await _seoService.GenerateRobotsTxtAsync(baseUrl);
        return Content(txt, "text/plain");
    }

    // ADMIN API ENDPOINTS

    [Authorize]
    [HttpGet("/api/seo")]
    public async Task<IActionResult> GetAll() => Ok(await _seoService.GetAllEntriesAsync());

    [Authorize]
    [HttpGet("/api/seo/{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entries = await _seoService.GetAllEntriesAsync();
        var entry = entries.FirstOrDefault(e => e.Id == id);
        if (entry == null) return NotFound();
        return Ok(entry);
    }

    [Authorize]
    [HttpPost("/api/seo")]
    public async Task<IActionResult> Create([FromBody] AIInsights.Models.SeoEntry entry)
    {
        if (!await IsAdminCallerAsync()) return Forbid();
        entry.Id = 0;
        var result = await _seoService.CreateOrUpdateAsync(entry);
        return Ok(result);
    }

    [Authorize]
    [HttpPut("/api/seo/{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] AIInsights.Models.SeoEntry entry)
    {
        if (!await IsAdminCallerAsync()) return Forbid();
        entry.Id = id;
        var result = await _seoService.CreateOrUpdateAsync(entry);
        return Ok(result);
    }

    [Authorize]
    [HttpDelete("/api/seo/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await IsAdminCallerAsync()) return Forbid();
        await _seoService.DeleteAsync(id);
        return Ok(new { success = true });
    }

    [Authorize]
    [HttpPost("/api/seo/seed")]
    public async Task<IActionResult> Seed()
    {
        if (!await IsAdminCallerAsync()) return Forbid();
        await _seoService.SeedDefaultEntriesAsync();
        return Ok(new { success = true });
    }

    [Authorize]
    [HttpGet("/api/seo/preview-sitemap")]
    public async Task<IActionResult> PreviewSitemap()
    {
        var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var xml = await _seoService.GenerateSitemapXmlAsync(baseUrl);
        return Content(xml, "application/xml");
    }
}
