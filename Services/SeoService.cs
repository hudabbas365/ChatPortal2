using ChatPortal2.Data;
using ChatPortal2.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace ChatPortal2.Services;

public interface ISeoService
{
    Task<List<SeoEntry>> GetAllEntriesAsync();
    Task<SeoEntry?> GetByUrlAsync(string pageUrl);
    Task<SeoEntry> CreateOrUpdateAsync(SeoEntry entry);
    Task DeleteAsync(int id);
    Task<string> GenerateSitemapXmlAsync(string baseUrl);
    Task<string> GenerateRobotsTxtAsync(string baseUrl);
    Task SeedDefaultEntriesAsync();
}

public class SeoService : ISeoService
{
    private readonly AppDbContext _db;

    public SeoService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<SeoEntry>> GetAllEntriesAsync()
    {
        return await _db.SeoEntries.OrderBy(s => s.PageUrl).ToListAsync();
    }

    public async Task<SeoEntry?> GetByUrlAsync(string pageUrl)
    {
        return await _db.SeoEntries.FirstOrDefaultAsync(s => s.PageUrl == pageUrl);
    }

    public async Task<SeoEntry> CreateOrUpdateAsync(SeoEntry entry)
    {
        var existing = await _db.SeoEntries.FirstOrDefaultAsync(s => s.Id == entry.Id);
        if (existing == null)
        {
            entry.CreatedAt = DateTime.UtcNow;
            entry.LastModified = DateTime.UtcNow;
            _db.SeoEntries.Add(entry);
        }
        else
        {
            existing.PageUrl = entry.PageUrl;
            existing.Title = entry.Title;
            existing.MetaDescription = entry.MetaDescription;
            existing.MetaKeywords = entry.MetaKeywords;
            existing.OgTitle = entry.OgTitle;
            existing.OgDescription = entry.OgDescription;
            existing.OgImage = entry.OgImage;
            existing.CanonicalUrl = entry.CanonicalUrl;
            existing.RobotsDirective = entry.RobotsDirective;
            existing.StructuredData = entry.StructuredData;
            existing.SitemapPriority = entry.SitemapPriority;
            existing.SitemapChangeFreq = entry.SitemapChangeFreq;
            existing.IncludeInSitemap = entry.IncludeInSitemap;
            existing.LastModified = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return existing ?? entry;
    }

    public async Task DeleteAsync(int id)
    {
        var entry = await _db.SeoEntries.FindAsync(id);
        if (entry != null)
        {
            _db.SeoEntries.Remove(entry);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<string> GenerateSitemapXmlAsync(string baseUrl)
    {
        var entries = await _db.SeoEntries.Where(s => s.IncludeInSitemap).OrderBy(s => s.PageUrl).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        foreach (var entry in entries)
        {
            var loc = baseUrl.TrimEnd('/') + entry.PageUrl;
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{System.Security.SecurityElement.Escape(loc)}</loc>");
            sb.AppendLine($"    <lastmod>{entry.LastModified:yyyy-MM-dd}</lastmod>");
            sb.AppendLine($"    <changefreq>{entry.SitemapChangeFreq}</changefreq>");
            sb.AppendLine($"    <priority>{entry.SitemapPriority:F1}</priority>");
            sb.AppendLine("  </url>");
        }

        sb.AppendLine("</urlset>");
        return sb.ToString();
    }

    public async Task<string> GenerateRobotsTxtAsync(string baseUrl)
    {
        return $"""
User-agent: *
Allow: /
Disallow: /api/
Disallow: /admin/
Disallow: /chat/embed

Sitemap: {baseUrl.TrimEnd('/')}/sitemap.xml
""";
    }

    public async Task SeedDefaultEntriesAsync()
    {
        var defaults = new[]
        {
            new SeoEntry
            {
                PageUrl = "/",
                Title = "ChatPortal2 — AI-Powered Data Conversations",
                MetaDescription = "Connect your datasources, chat with AI agents, and visualize insights with 72+ chart types.",
                MetaKeywords = "AI chat, data analytics, dashboards, charts, agents",
                OgTitle = "ChatPortal2 — AI-Powered Data Conversations",
                OgDescription = "Connect your datasources, chat with AI agents, and visualize insights.",
                SitemapPriority = 1.0m,
                SitemapChangeFreq = "daily",
                CreatedBy = "system"
            },
            new SeoEntry
            {
                PageUrl = "/about",
                Title = "About ChatPortal2 — Our Mission",
                MetaDescription = "Learn about our mission to democratize data analytics with AI-powered conversations.",
                MetaKeywords = "about, mission, AI analytics",
                OgTitle = "About ChatPortal2",
                OgDescription = "Learn about our mission to democratize data analytics.",
                SitemapPriority = 0.8m,
                SitemapChangeFreq = "monthly",
                CreatedBy = "system"
            },
            new SeoEntry
            {
                PageUrl = "/pricing",
                Title = "Pricing — ChatPortal2 Plans",
                MetaDescription = "Choose the plan that fits your needs. Start with a 30-day free trial.",
                MetaKeywords = "pricing, plans, free trial, subscription",
                OgTitle = "ChatPortal2 Pricing",
                OgDescription = "Choose the plan that fits your needs. Start with a 30-day free trial.",
                SitemapPriority = 0.9m,
                SitemapChangeFreq = "weekly",
                CreatedBy = "system"
            },
            new SeoEntry
            {
                PageUrl = "/docs",
                Title = "Documentation — ChatPortal2",
                MetaDescription = "Learn how to connect datasources, create agents, and build dashboards.",
                MetaKeywords = "documentation, docs, help, guide",
                OgTitle = "ChatPortal2 Documentation",
                OgDescription = "Learn how to use ChatPortal2.",
                SitemapPriority = 0.7m,
                SitemapChangeFreq = "weekly",
                CreatedBy = "system"
            },
            new SeoEntry
            {
                PageUrl = "/blog",
                Title = "Blog — ChatPortal2 Updates",
                MetaDescription = "Latest updates, tips, and insights from the ChatPortal2 team.",
                MetaKeywords = "blog, updates, tips, news",
                OgTitle = "ChatPortal2 Blog",
                OgDescription = "Latest updates, tips, and insights from the ChatPortal2 team.",
                SitemapPriority = 0.6m,
                SitemapChangeFreq = "daily",
                CreatedBy = "system"
            }
        };

        foreach (var entry in defaults)
        {
            if (!await _db.SeoEntries.AnyAsync(s => s.PageUrl == entry.PageUrl))
            {
                _db.SeoEntries.Add(entry);
            }
        }

        await _db.SaveChangesAsync();
    }
}
