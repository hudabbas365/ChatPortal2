using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AIInsights.Services;

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
        var byUrl = entries.ToDictionary(e => e.PageUrl, StringComparer.OrdinalIgnoreCase);

        // Dynamically include every published DocArticle / BlogPost even if its
        // matching SeoEntry is missing (e.g. seeded outside the admin UI).
        var docUrls = await _db.DocArticles
            .Where(d => d.IsPublished)
            .Select(d => new { Url = "/docs/" + d.Slug, LastMod = d.UpdatedAt })
            .ToListAsync();
        var blogUrls = await _db.BlogPosts
            .Where(b => b.IsPublished)
            .Select(b => new { Url = "/blog/" + b.Slug, LastMod = b.PublishedAt })
            .ToListAsync();

        foreach (var d in docUrls)
        {
            if (!byUrl.ContainsKey(d.Url))
            {
                var virt = new SeoEntry
                {
                    PageUrl = d.Url,
                    LastModified = d.LastMod,
                    SitemapChangeFreq = "monthly",
                    SitemapPriority = 0.7m,
                    IncludeInSitemap = true
                };
                entries.Add(virt);
                byUrl[d.Url] = virt;
            }
        }
        foreach (var b in blogUrls)
        {
            if (!byUrl.ContainsKey(b.Url))
            {
                var virt = new SeoEntry
                {
                    PageUrl = b.Url,
                    LastModified = b.LastMod,
                    SitemapChangeFreq = "weekly",
                    SitemapPriority = 0.8m,
                    IncludeInSitemap = true
                };
                entries.Add(virt);
                byUrl[b.Url] = virt;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        foreach (var entry in entries.OrderBy(e => e.PageUrl))
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
                Title = "AIInsights — AI-Powered Data Conversations",
                MetaDescription = "Connect your datasources, chat with AI agents, and visualize insights with 72+ chart types.",
                MetaKeywords = "AI chat, data analytics, dashboards, charts, agents",
                OgTitle = "AIInsights — AI-Powered Data Conversations",
                OgDescription = "Connect your datasources, chat with AI agents, and visualize insights.",
                SitemapPriority = 1.0m,
                SitemapChangeFreq = "daily",
                CreatedBy = "system"
            },
            new SeoEntry
            {
                PageUrl = "/about",
                Title = "About AIInsights — Our Mission",
                MetaDescription = "Learn about our mission to democratize data analytics with AI-powered conversations.",
                MetaKeywords = "about, mission, AI analytics",
                OgTitle = "About AIInsights",
                OgDescription = "Learn about our mission to democratize data analytics.",
                SitemapPriority = 0.8m,
                SitemapChangeFreq = "monthly",
                CreatedBy = "system"
            },
            new SeoEntry
            {
                PageUrl = "/pricing",
                Title = "Pricing — AIInsights Plans",
                MetaDescription = "Choose the plan that fits your needs. Start with a 30-day free trial.",
                MetaKeywords = "pricing, plans, free trial, subscription",
                OgTitle = "AIInsights Pricing",
                OgDescription = "Choose the plan that fits your needs. Start with a 30-day free trial.",
                SitemapPriority = 0.9m,
                SitemapChangeFreq = "weekly",
                CreatedBy = "system"
            },
            new SeoEntry
            {
                PageUrl = "/docs",
                Title = "Documentation — AIInsights",
                MetaDescription = "Learn how to connect datasources, create agents, and build dashboards.",
                MetaKeywords = "documentation, docs, help, guide",
                OgTitle = "AIInsights Documentation",
                OgDescription = "Learn how to use AIInsights.",
                SitemapPriority = 0.7m,
                SitemapChangeFreq = "weekly",
                CreatedBy = "system"
            },
            new SeoEntry
            {
                PageUrl = "/blog",
                Title = "Blog — AIInsights Updates",
                MetaDescription = "Latest updates, tips, and insights from the AIInsights team.",
                MetaKeywords = "blog, updates, tips, news",
                OgTitle = "AIInsights Blog",
                OgDescription = "Latest updates, tips, and insights from the AIInsights team.",
                SitemapPriority = 0.6m,
                SitemapChangeFreq = "daily",
                CreatedBy = "system"
            },
            new SeoEntry
            {
                PageUrl = "/terms",
                Title = "Terms & Conditions — AIInsights",
                MetaDescription = "Terms of use, billing, cancellation and no-refund policy for AIInsights365.",
                MetaKeywords = "terms, conditions, refund, billing, cancellation",
                OgTitle = "AIInsights Terms & Conditions",
                OgDescription = "Terms of use, billing, cancellation and no-refund policy.",
                SitemapPriority = 0.3m,
                SitemapChangeFreq = "yearly",
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
