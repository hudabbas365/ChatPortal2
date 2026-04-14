namespace AIInsights.Models;

public class SeoEntry
{
    public int Id { get; set; }
    public string PageUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public string MetaDescription { get; set; } = "";
    public string MetaKeywords { get; set; } = "";
    public string OgTitle { get; set; } = "";
    public string OgDescription { get; set; } = "";
    public string OgImage { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";
    public string RobotsDirective { get; set; } = "index, follow";
    public string StructuredData { get; set; } = "";
    public decimal SitemapPriority { get; set; } = 0.5m;
    public string SitemapChangeFreq { get; set; } = "monthly";
    public bool IncludeInSitemap { get; set; } = true;
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "";
}
