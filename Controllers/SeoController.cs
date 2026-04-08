using ChatPortal2.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChatPortal2.Controllers;

public class SeoController : Controller
{
    private readonly ISeoService _seoService;
    private readonly IConfiguration _config;

    public SeoController(ISeoService seoService, IConfiguration config)
    {
        _seoService = seoService;
        _config = config;
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

    [HttpGet("/api/seo")]
    public async Task<IActionResult> GetAll() => Ok(await _seoService.GetAllEntriesAsync());

    [HttpGet("/api/seo/{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entries = await _seoService.GetAllEntriesAsync();
        var entry = entries.FirstOrDefault(e => e.Id == id);
        if (entry == null) return NotFound();
        return Ok(entry);
    }

    [HttpPost("/api/seo")]
    public async Task<IActionResult> Create([FromBody] ChatPortal2.Models.SeoEntry entry)
    {
        entry.Id = 0;
        var result = await _seoService.CreateOrUpdateAsync(entry);
        return Ok(result);
    }

    [HttpPut("/api/seo/{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] ChatPortal2.Models.SeoEntry entry)
    {
        entry.Id = id;
        var result = await _seoService.CreateOrUpdateAsync(entry);
        return Ok(result);
    }

    [HttpDelete("/api/seo/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _seoService.DeleteAsync(id);
        return Ok(new { success = true });
    }

    [HttpPost("/api/seo/seed")]
    public async Task<IActionResult> Seed()
    {
        await _seoService.SeedDefaultEntriesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("/api/seo/preview-sitemap")]
    public async Task<IActionResult> PreviewSitemap()
    {
        var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        var xml = await _seoService.GenerateSitemapXmlAsync(baseUrl);
        return Content(xml, "application/xml");
    }
}
