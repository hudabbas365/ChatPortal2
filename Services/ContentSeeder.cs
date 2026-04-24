using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIInsights.Services;

public interface IContentSeeder
{
    Task SeedAsync();
}

/// <summary>
/// Seeds release-note style documentation articles and blog posts that describe
/// AIInsights365.net Phase 1 capabilities and the Phase 2 roadmap. Every record
/// also gets a matching <see cref="SeoEntry"/> so the URL is emitted in sitemap.xml.
/// Safe to run on every startup — existing rows (matched by Slug / PageUrl) are
/// left untouched.
/// </summary>
public class ContentSeeder : IContentSeeder
{
    private readonly AppDbContext _db;
    private readonly ILogger<ContentSeeder> _log;

    public ContentSeeder(AppDbContext db, ILogger<ContentSeeder> log)
    {
        _db = db;
        _log = log;
    }

    public async Task SeedAsync()
    {
        await EnsureTablesAsync();
        await SeedDocsAsync();
        await SeedBlogAsync();
    }

    // Guarantees the BlogPosts and DocArticles tables exist even when the current
    // EF migration history does not include a CreateTable for them (e.g. after a
    // rebased migration chain). Uses idempotent SQL Server DDL so it is safe on
    // every startup.
    private async Task EnsureTablesAsync()
    {
        const string sql = @"
IF OBJECT_ID(N'[dbo].[BlogPosts]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BlogPosts] (
        [Id]          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Title]       NVARCHAR(400)  NOT NULL,
        [Slug]        NVARCHAR(200)  NOT NULL,
        [Summary]     NVARCHAR(1000) NOT NULL,
        [Content]     NVARCHAR(MAX)  NOT NULL,
        [Author]      NVARCHAR(200)  NULL,
        [ImageUrl]    NVARCHAR(1000) NULL,
        [PublishedAt] DATETIME2      NOT NULL,
        [IsPublished] BIT            NOT NULL
    );
    CREATE UNIQUE INDEX [IX_BlogPosts_Slug] ON [dbo].[BlogPosts]([Slug]);
END;

IF OBJECT_ID(N'[dbo].[DocArticles]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[DocArticles] (
        [Id]          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Title]       NVARCHAR(400)  NOT NULL,
        [Slug]        NVARCHAR(200)  NOT NULL,
        [Summary]     NVARCHAR(1000) NOT NULL,
        [Content]     NVARCHAR(MAX)  NOT NULL,
        [Author]      NVARCHAR(200)  NULL,
        [SortOrder]   INT            NOT NULL,
        [CreatedAt]   DATETIME2      NOT NULL,
        [UpdatedAt]   DATETIME2      NOT NULL,
        [IsPublished] BIT            NOT NULL
    );
    CREATE UNIQUE INDEX [IX_DocArticles_Slug] ON [dbo].[DocArticles]([Slug]);
END;";
        try
        {
            await _db.Database.ExecuteSqlRawAsync(sql);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ContentSeeder: could not ensure BlogPosts/DocArticles tables exist.");
        }
    }

    // ───────────────────────────── Docs ─────────────────────────────
    private async Task SeedDocsAsync()
    {
        var now = DateTime.UtcNow;
        var docs = new[]
        {
            new DocArticle
            {
                Title = "Release Notes — AIInsights365.net v1.0 (Phase 1)",
                Slug = "release-notes-v1-phase-1",
                Summary = "Everything that shipped in the first public release of AIInsights365.net — AI-powered chat, 72+ chart types, agent workspaces, and SEO-ready content pipelines.",
                Author = "AIInsights365 Team",
                SortOrder = 1,
                Content = """
<h2>AIInsights365.net — The AI Analytics Platform You've Been Waiting For</h2>
<p><strong>AIInsights365.net</strong> delivers a conversational analytics experience powered by state-of-the-art AI. Phase 1 lays the foundation: connect your data, chat with intelligent agents, and visualize answers instantly.</p>

<h3>✨ What's New in v1.0</h3>
<ul>
  <li><strong>AI Chat over your data</strong> — natural-language questions answered by agents tuned to your datasource.</li>
  <li><strong>72+ chart types</strong> via the enhanced chart library (bar, line, pie, treemap, sankey, heatmap, radar, funnel, waterfall and more).</li>
  <li><strong>Workspaces & Agents</strong> — organize datasources, pin results, and collaborate with teammates.</li>
  <li><strong>Auto Report Generator</strong> — AI drafts a full multi-page report from a single prompt.</li>
  <li><strong>Q&amp;A side panel on every report</strong> with streaming responses and a draggable FAB.</li>
  <li><strong>Organization + Super-Admin portals</strong> with full RBAC, trial enforcement, and token budgeting.</li>
  <li><strong>PayPal billing</strong> with subscription plans and a 30-day free trial.</li>
  <li><strong>SEO engine</strong> — every page, doc and blog post is indexed into <code>sitemap.xml</code> automatically.</li>
</ul>

<h3>🤖 AI Capabilities Highlight</h3>
<p>AIInsights365.net is built from the ground up around AI. Our agents understand schema, infer joins, pick the right visualization, and explain their reasoning in plain English — turning every user into a data analyst.</p>

<h3>Next Up</h3>
<p>Phase 2 brings <strong>15 new datasource connectors</strong> and the <strong>On-Prem Data Gateway</strong>. See the blog for the full roadmap.</p>
""",
                IsPublished = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new DocArticle
            {
                Title = "Getting Started with AI Chat & Agents",
                Slug = "getting-started-ai-chat-agents",
                Summary = "Create your first workspace, wire up a datasource, and ask AIInsights365.net your first natural-language question in under 5 minutes.",
                Author = "AIInsights365 Team",
                SortOrder = 2,
                Content = """
<h2>From Zero to Insights in 5 Minutes</h2>
<p>This guide walks you through the Phase 1 experience on <strong>AIInsights365.net</strong>.</p>

<h3>1. Create a Workspace</h3>
<p>Workspaces isolate data and collaborators. Head to <em>Workspaces → New</em> and give it a name.</p>

<h3>2. Connect a Datasource</h3>
<p>Phase 1 supports SQL Server, PostgreSQL, MySQL, SQLite, and CSV uploads. More are coming in Phase 2.</p>

<h3>3. Spin Up an AI Agent</h3>
<p>Agents are AI personalities scoped to a datasource. They learn your schema and answer questions conversationally.</p>

<h3>4. Ask Anything</h3>
<blockquote>"Show me revenue by region for the last 6 months as a stacked bar chart"</blockquote>
<p>The agent writes the query, runs it, picks the chart, and streams the answer back. Pin the result to a dashboard with one click.</p>

<h3>Why AIInsights365.net?</h3>
<p>We put <strong>AI first</strong>: no query builders, no dashboard-authoring drudgery. Just conversation.</p>
""",
                IsPublished = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new DocArticle
            {
                Title = "Chart Library — 72+ Visualizations",
                Slug = "chart-library-overview",
                Summary = "A tour of the enhanced chart library that ships with AIInsights365.net Phase 1, including interactive, AI-recommended visualizations.",
                Author = "AIInsights365 Team",
                SortOrder = 3,
                Content = """
<h2>72+ Chart Types, One API</h2>
<p><strong>AIInsights365.net</strong> ships with a unified chart renderer covering the full analytics spectrum:</p>
<ul>
  <li><strong>Comparison</strong> — bar, column, grouped, stacked, percent-stacked.</li>
  <li><strong>Trend</strong> — line, area, smooth line, step, spline, candlestick.</li>
  <li><strong>Composition</strong> — pie, donut, sunburst, treemap, waterfall.</li>
  <li><strong>Distribution</strong> — histogram, box plot, violin, scatter, bubble.</li>
  <li><strong>Flow</strong> — sankey, chord, network graph, funnel.</li>
  <li><strong>Geospatial</strong> — choropleth, bubble map, heat map.</li>
  <li><strong>KPI</strong> — gauge, bullet, scorecard, sparkline.</li>
</ul>

<h3>AI-Driven Chart Selection</h3>
<p>Our AI picks the right chart for your question automatically. Override it anytime from the properties panel.</p>

<h3>Performance</h3>
<p>All charts render on a shared canvas pipeline with virtualization for large datasets — a key differentiator of <strong>AIInsights365.net</strong>.</p>
""",
                IsPublished = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new DocArticle
            {
                Title = "Reports, Dashboards & the AI Q&A Panel",
                Slug = "reports-dashboards-qa-panel",
                Summary = "Auto-generate reports, pin charts to dashboards, and keep asking follow-up questions via the floating Q&A panel — all powered by AIInsights365.net AI.",
                Author = "AIInsights365 Team",
                SortOrder = 4,
                Content = """
<h2>Reports That Answer Back</h2>
<p>On <strong>AIInsights365.net</strong>, a report is never a dead artifact — it is a conversation anchored to your data.</p>

<h3>Auto Report Generator</h3>
<p>Describe what you want ("quarterly sales review with regional breakdown and anomaly callouts") and AI drafts the full report, including titles, narrative, and charts.</p>

<h3>Dashboards</h3>
<p>Pin any chat response as a tile. Arrange, resize, and theme — dashboards honor your organization's brand.</p>

<h3>The Q&amp;A Floating Panel</h3>
<p>Every report carries a draggable AI assistant. Ask follow-ups, request alternate chart types, or export findings — the panel streams answers with zero page reloads.</p>

<h3>Share Securely</h3>
<p>Create tokenized share links for external stakeholders without exposing credentials or raw data.</p>
""",
                IsPublished = true,
                CreatedAt = now,
                UpdatedAt = now
            }
        };

        int added = 0;
        foreach (var d in docs)
        {
            if (!await _db.DocArticles.AnyAsync(x => x.Slug == d.Slug))
            {
                _db.DocArticles.Add(d);
                added++;
            }
            await EnsureSeoAsync(
                pageUrl: $"/docs/{d.Slug}",
                title: $"{d.Title} — AIInsights365.net",
                description: d.Summary,
                keywords: "AIInsights365, AI analytics, documentation, AI insights, data AI, " + d.Slug.Replace('-', ' '),
                priority: 0.7m,
                changeFreq: "monthly");
        }
        await _db.SaveChangesAsync();
        _log.LogInformation("ContentSeeder: inserted {Count} DocArticle(s).", added);
    }

    // ───────────────────────────── Blog ─────────────────────────────
    private async Task SeedBlogAsync()
    {
        var posts = new[]
        {
            new BlogPost
            {
                Title = "Introducing AIInsights365.net — Phase 1 Is Live",
                Slug = "introducing-aiinsights365-phase-1",
                Summary = "A preview of everything that landed in Phase 1: AI chat, 72+ charts, agent workspaces, auto reports, and a content-rich SEO engine.",
                Author = "AIInsights365 Team",
                PublishedAt = DateTime.UtcNow.AddDays(-6),
                IsPublished = true,
                Content = """
<h2>AIInsights365.net — Phase 1 Preview</h2>
<p>We are thrilled to announce that <strong>AIInsights365.net</strong> Phase 1 is live. Our mission is simple: make every employee a data analyst by putting <strong>AI at the center of analytics</strong>.</p>

<h3>What Phase 1 Delivers</h3>
<ul>
  <li>AI-powered chat over SQL and file-based datasources.</li>
  <li>72+ interactive chart types with AI-assisted chart selection.</li>
  <li>Workspaces, agents, pinned results, and dashboards.</li>
  <li>Auto report generator that drafts multi-page narratives.</li>
  <li>Draggable Q&amp;A floating panel on every report.</li>
  <li>Organization/Super-Admin portals, trial enforcement, token budgeting.</li>
  <li>PayPal billing with a 30-day free trial.</li>
</ul>

<h3>AI Everywhere</h3>
<p>From question to chart to narrative, AI drives every step. <strong>AIInsights365.net</strong> is not a dashboard tool with AI bolted on — it is an AI platform that happens to produce dashboards.</p>

<h3>Coming in Phase 2</h3>
<p><strong>15 new datasource connectors</strong> and the <strong>On-Prem Data Gateway</strong>. Read on.</p>
"""
            },
            new BlogPost
            {
                Title = "Phase 2 Roadmap — 15 New Datasources Coming to AIInsights365.net",
                Slug = "phase-2-roadmap-15-datasources",
                Summary = "Snowflake, BigQuery, Databricks, Redshift, Oracle, SAP HANA, MongoDB, Elasticsearch, ClickHouse, Salesforce, HubSpot, Google Analytics, Stripe, Shopify, and REST — all coming in Phase 2.",
                Author = "AIInsights365 Team",
                PublishedAt = DateTime.UtcNow.AddDays(-4),
                IsPublished = true,
                Content = """
<h2>15 Datasources. One AI Conversation.</h2>
<p>Phase 2 of <strong>AIInsights365.net</strong> expands our connector catalog from a handful to <strong>15 production-grade datasources</strong>, so your AI agents can answer questions across every corner of your stack.</p>

<h3>The Phase 2 Connector Lineup</h3>
<ol>
  <li>Snowflake</li>
  <li>Google BigQuery</li>
  <li>Databricks (Delta Lake)</li>
  <li>Amazon Redshift</li>
  <li>Oracle Database</li>
  <li>SAP HANA</li>
  <li>MongoDB</li>
  <li>Elasticsearch / OpenSearch</li>
  <li>ClickHouse</li>
  <li>Salesforce</li>
  <li>HubSpot</li>
  <li>Google Analytics 4</li>
  <li>Stripe</li>
  <li>Shopify</li>
  <li>Generic REST / GraphQL</li>
</ol>

<h3>AI That Understands Every Schema</h3>
<p>Each connector ships with a metadata importer so our agents instantly understand table relationships, field semantics, and sample values — delivering the same <strong>AIInsights365.net</strong> magic regardless of where your data lives.</p>

<h3>Next: Bridging to On-Prem</h3>
<p>Many of these sources live behind corporate firewalls. That's where the Phase 2 <strong>On-Prem Data Gateway</strong> comes in — covered in the next post.</p>
"""
            },
            new BlogPost
            {
                Title = "The On-Prem Data Gateway — Phase 2 Deep Dive",
                Slug = "on-prem-data-gateway-phase-2",
                Summary = "How the AIInsights365.net gateway securely bridges cloud AI to on-premises SQL Server, Oracle, SAP, and file shares — without opening a single inbound firewall port.",
                Author = "AIInsights365 Team",
                PublishedAt = DateTime.UtcNow.AddDays(-2),
                IsPublished = true,
                Content = """
<h2>Bring Your On-Prem Data to AIInsights365.net — Securely</h2>
<p>Your most valuable data often lives behind the firewall. Phase 2 introduces the <strong>AIInsights365.net On-Prem Data Gateway</strong>, a lightweight Windows/Linux service that brokers queries between our cloud AI and your private network.</p>

<h3>How It Works</h3>
<ul>
  <li><strong>Outbound-only</strong> TLS tunnel — no inbound ports to open.</li>
  <li>End-to-end encryption with per-tenant keys.</li>
  <li>Local credential vault — secrets never leave your datacenter.</li>
  <li>Query allow-lists, row-level policies, and full audit logging.</li>
  <li>High availability via active/passive gateway clustering.</li>
</ul>

<h3>Supported Sources Behind the Gateway</h3>
<p>Every Phase 2 connector — SQL Server, Oracle, SAP HANA, MongoDB, file shares, and more — can run through the gateway, so <strong>AIInsights365.net AI agents</strong> work the same whether your data is in the cloud or in your server room.</p>

<h3>Why It Matters</h3>
<p>AI is only as powerful as the data it can reach. The gateway removes the last blocker to putting conversational analytics in front of your entire enterprise.</p>
"""
            },
            new BlogPost
            {
                Title = "Why AIInsights365.net Is Different — An AI-First Analytics Platform",
                Slug = "why-aiinsights365-ai-first",
                Summary = "Legacy BI tools bolt AI on as a feature. AIInsights365.net is architected around AI from the ground up — and that changes everything about how teams get answers.",
                Author = "AIInsights365 Team",
                PublishedAt = DateTime.UtcNow.AddDays(-1),
                IsPublished = true,
                Content = """
<h2>AI-First, Not AI-Added</h2>
<p>Most analytics vendors spent the last decade building dashboard editors. We spent ours building <strong>AIInsights365.net</strong> — an AI-native platform where the AI is the product, not a plugin.</p>

<h3>The AI Capabilities That Set Us Apart</h3>
<ul>
  <li><strong>Schema-aware agents</strong> that reason about joins, metrics, and time grains.</li>
  <li><strong>Streaming answers</strong> with cited SQL and transparent reasoning.</li>
  <li><strong>AI chart selection</strong> — the right visualization, every time.</li>
  <li><strong>Auto Report Generator</strong> — a full report from a single prompt.</li>
  <li><strong>Conversational follow-ups</strong> via the floating Q&amp;A panel on every artifact.</li>
  <li><strong>Token-budgeted</strong> AI so costs are predictable at enterprise scale.</li>
</ul>

<h3>What This Unlocks for Your Team</h3>
<p>Non-technical users ask questions in English. Analysts accelerate 10×. Executives get narratives, not spreadsheets. That is the promise of <strong>AIInsights365.net</strong>.</p>

<h3>Phase 2 and Beyond</h3>
<p>With 15 new connectors and the On-Prem Gateway landing in Phase 2, every piece of your data becomes fair game for our AI. Join us at <a href="https://aiinsights365.net">AIInsights365.net</a>.</p>
"""
            }
        };

        int added = 0;
        foreach (var p in posts)
        {
            if (!await _db.BlogPosts.AnyAsync(x => x.Slug == p.Slug))
            {
                _db.BlogPosts.Add(p);
                added++;
            }
            await EnsureSeoAsync(
                pageUrl: $"/blog/{p.Slug}",
                title: $"{p.Title} — AIInsights365.net",
                description: p.Summary,
                keywords: "AIInsights365, AI analytics, AI insights, phase 1, phase 2, data gateway, on-prem, " + p.Slug.Replace('-', ' '),
                priority: 0.8m,
                changeFreq: "weekly");
        }
        await _db.SaveChangesAsync();
        _log.LogInformation("ContentSeeder: inserted {Count} BlogPost(s).", added);
    }

    // ───────────────────── SEO helper ─────────────────────
    private async Task EnsureSeoAsync(string pageUrl, string title, string description,
        string keywords, decimal priority, string changeFreq)
    {
        if (await _db.SeoEntries.AnyAsync(s => s.PageUrl == pageUrl))
            return;

        _db.SeoEntries.Add(new SeoEntry
        {
            PageUrl = pageUrl,
            Title = title,
            MetaDescription = description,
            MetaKeywords = keywords,
            OgTitle = title,
            OgDescription = description,
            SitemapPriority = priority,
            SitemapChangeFreq = changeFreq,
            IncludeInSitemap = true,
            CreatedBy = "system",
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow
        });
    }
}
