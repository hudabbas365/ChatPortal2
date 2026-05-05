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
/// Runs an upsert on every startup — existing rows (matched by Slug) are updated
/// in-place; new rows are inserted. PublishedAt on BlogPosts is preserved if already set.
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
                Title       = "Release Notes \u2014 AIInsights365.net v1.0 (Phase 1)",
                Slug        = "release-notes-v1-phase-1",
                Summary     = "A comprehensive executive overview of every capability shipped in Phase 1: AI-powered chat, 72+ chart types, dashboards, agents, hosting topology, and enterprise user management.",
                Author      = "AIInsights365 Team",
                SortOrder   = 1,
                IsPublished = true,
                CreatedAt   = now,
                UpdatedAt   = now,
                Content     = """
<h2>Release Notes &mdash; AIInsights365.net v1.0 (Phase 1)</h2>
<p>
  We are proud to announce the general availability of <strong>AIInsights365.net v1.0</strong>, the first
  production release of our AI-native analytics platform. Phase 1 represents eighteen months of focused
  engineering, user research, and iterative design &mdash; all in service of one goal: to let any person,
  regardless of SQL expertise, have a genuine conversation with their data and receive answers they can trust.
  This document provides an executive summary of every capability that shipped in Phase 1, organized by
  feature area. Each section links to dedicated documentation for teams that want to go deeper.
</p>

<h2>1. Datasource Connectivity</h2>
<p>
  Phase 1 ships with native connectors for the five most common data environments encountered in mid-market
  and enterprise organizations: <strong>Microsoft SQL Server</strong>, <strong>PostgreSQL</strong>,
  <strong>MySQL</strong>, <strong>SQLite</strong>, and <strong>CSV file uploads</strong>. Connections follow
  a three-stage lifecycle: <em>Validate</em> (credentials and network reachability are confirmed),
  <em>Introspect</em> (the AIInsights365.net engine walks every table, column, index, and foreign-key
  relationship in the target schema and stores a structured metadata snapshot), and <em>Ready</em> (the
  datasource is available to agents and users).
</p>
<p>
  Connection credentials are encrypted at rest using AES-256 with per-tenant key material stored in Azure
  Key Vault. The schema introspection cache is refreshed on demand or on a configurable schedule. During
  setup, administrators can preview a sample of rows from each table to verify connectivity before exposing
  the datasource to the broader team. All supported connection types accept both direct TCP connections and
  SSL/TLS-wrapped tunnels, ensuring compatibility with cloud-hosted and self-hosted database environments.
</p>

<h2>2. Ask AI &mdash; Conversational Query Engine</h2>
<p>
  The centerpiece of Phase 1 is the <strong>Ask AI</strong> conversational engine. Users compose
  natural-language questions in the chat interface &mdash; for example, <em>&ldquo;Show me monthly revenue
  by region for the last two years, excluding returns&rdquo;</em> &mdash; and AIInsights365.net translates
  the request into a semantically correct SQL query, executes it against the live datasource, selects the
  most appropriate visualization, and streams the full answer back to the browser in real time.
</p>
<p>
  Every response includes a <strong>Reasoning Panel</strong> that exposes the generated SQL, the column-to-field
  mappings the model applied, and a plain-language explanation of the query logic. This transparency is
  non-negotiable: analysts need to audit AI answers, and the Reasoning Panel gives them everything they need
  to verify correctness. Token budgets are configurable at the organization and user levels so platform costs
  remain predictable. Follow-up questions maintain full conversation context, enabling iterative analysis
  without page reloads.
</p>

<h2>3. AI Agents</h2>
<p>
  Agents are configurable AI personalities scoped to a single datasource. Each agent carries a system prompt
  authored by an administrator, a copy of the relevant schema metadata injected automatically at conversation
  start, and a persona name visible to end users. Organizations can maintain multiple agents &mdash; for
  example, a <em>Sales Agent</em> tuned to the CRM schema and a <em>Finance Agent</em> tuned to the ERP
  schema &mdash; and users select the appropriate agent from a workspace drop-down before starting a
  conversation.
</p>
<p>
  Agent system prompts may include business-specific context such as metric definitions, fiscal calendar
  conventions, and data quality caveats, which significantly improves answer accuracy for domain-specific
  questions. Agents are managed in the <strong>Agent Configuration</strong> section of the platform, where
  administrators can create, edit, clone, and deactivate agents without restarting the service. Each
  organization may configure up to ten active agents on the Enterprise plan.
</p>

<h2>4. Chart Library &mdash; 72+ Visualization Types</h2>
<p>
  AIInsights365.net ships with a chart library spanning <strong>72 distinct visualization types</strong>
  across seven semantic categories. The categories are: <em>Comparison</em> (bar, column, grouped bar,
  stacked bar, percent-stacked bar, lollipop), <em>Trend</em> (line, area, smooth-line, step line, spline,
  candlestick, range area), <em>Composition</em> (pie, donut, sunburst, treemap, waterfall, Marimekko),
  <em>Distribution</em> (histogram, box plot, violin plot, scatter, bubble, density plot), <em>Flow</em>
  (Sankey, chord, network graph, funnel, parallel coordinates), <em>Geospatial</em> (choropleth map, bubble
  map, point cluster, heatmap overlay), and <em>KPI</em> (gauge, bullet chart, scorecard, sparkline,
  progress ring).
</p>
<p>
  The AI model selects the most appropriate chart type based on the semantic content of the question and
  the shape of the result set. Users can override the AI recommendation at any time using the Chart
  Properties panel. All 72 types share a single unified canvas-based rendering pipeline with virtual
  scrolling and optional WebGL acceleration, ensuring consistent performance even with large datasets
  exceeding one million rows.
</p>

<h2>5. Dashboards</h2>
<p>
  Any chat response &mdash; chart, table, KPI card, or narrative text &mdash; can be pinned to a dashboard
  with a single click. Dashboards support drag-to-reorder and resize-handle tile manipulation on a
  responsive column grid, with breakpoints that adapt layouts to tablet and mobile viewports. Organization
  administrators can apply a brand theme (logo, primary and accent color palette, typography) that is
  inherited by all dashboards in that organization&rsquo;s workspace.
</p>
<p>
  Dashboard-level filter controls &mdash; including date range pickers, region and category selectors, and
  free-text search &mdash; propagate to all tiles simultaneously, keeping the entire dashboard synchronized
  with a single filter state. Dashboards can be shared with external stakeholders using tokenized share
  links that expire on a configurable schedule and can be revoked instantly from the sharing management
  panel. Read-only shared views require no account and load in under two seconds on a standard connection.
</p>

<h2>6. Hosting Topology</h2>
<p>
  The AIInsights365.net Phase 1 deployment consists of three distinct applications: the <strong>Web
  Application</strong> (the primary user-facing interface), the <strong>Super-Admin Portal</strong> (a
  separate secured application for platform administrators managing organizations, plans, and quotas), and
  the <strong>Gateway App</strong> (a reverse-proxy and request-routing layer that enforces tenant
  isolation, rate limiting, and token budget accounting). All three applications are stateless and can be
  scaled horizontally behind a load balancer. The backend data store is a multi-tenant Microsoft SQL Server
  database with row-level tenant isolation enforced at the application layer.
</p>
<p>
  TLS termination occurs at the load balancer. Secrets and connection strings are externalized through
  environment variables and Azure App Configuration, meaning no credentials are ever embedded in
  application binaries or container images. Health-check endpoints on each application enable zero-downtime
  rolling deployments through any standard orchestrator.
</p>

<h2>7. User Settings &amp; RBAC</h2>
<p>
  Phase 1 ships a comprehensive user management layer. Individual users control their profile (display
  name, avatar, email), password, two-factor authentication (TOTP-based, compatible with standard
  authenticator apps such as Google Authenticator and Authy), and notification preferences. Access to
  platform capabilities is governed by four RBAC roles: <em>Viewer</em> (read-only access to shared
  dashboards and reports), <em>Analyst</em> (full Ask AI access, can pin to personal dashboards),
  <em>Editor</em> (can create and share team dashboards and reports), and <em>Admin</em> (full
  administrative access including agent management, datasource management, and user role assignments).
</p>
<p>
  Subscriptions are managed through a PayPal integration supporting monthly and annual billing cycles, with
  a <strong>30-day free trial</strong> on all paid plans. Token quotas are set per plan and can be
  overridden per user by organization administrators. Usage telemetry is visible in the Admin dashboard in
  real time, giving administrators full visibility into token consumption before bills arrive.
</p>

<h2>Phase 2 Roadmap Preview</h2>
<p>
  Phase 2 is already in active development. The two flagship capabilities are a <strong>TOML-based
  configuration system</strong> for datasources and agents (replacing JSON-based config with a
  human-friendly, diff-friendly format), and an <strong>AI Insight ETL pipeline powered by DAX
  queries</strong> that extracts curated business metrics from tabular models and loads them into a
  structured insight store for AI agents to consume. Phase 2 also expands the connector catalog to fifteen
  datasources including Snowflake, BigQuery, Databricks, Salesforce, and Stripe. Visit the
  AIInsights365.net blog for the full Phase 2 roadmap walkthrough.
</p>
"""
            },
            new DocArticle
            {
                Title       = "Getting Started with AI Chat, Agents &amp; User Settings",
                Slug        = "getting-started-ai-chat-agents",
                Summary     = "Step-by-step guidance on connecting a datasource, spinning up an AI agent, querying your data in natural language, and configuring user settings and access control.",
                Author      = "AIInsights365 Team",
                SortOrder   = 2,
                IsPublished = true,
                CreatedAt   = now,
                UpdatedAt   = now,
                Content     = """
<h2>Getting Started with AIInsights365.net</h2>
<p>
  This guide is for administrators and analysts who are logging in to <strong>AIInsights365.net</strong>
  for the first time. By the end of these five steps you will have a live datasource, a configured AI
  agent, and a pinned chart on your first dashboard &mdash; all without writing a single line of SQL.
  The entire process typically takes under fifteen minutes on a fresh organization account.
</p>

<h2>Step 1: Connecting a Datasource</h2>
<p>
  Navigate to <strong>Settings &rarr; Datasources &rarr; New Datasource</strong>. Choose your database
  engine from the supported list: Microsoft SQL Server, PostgreSQL, MySQL, SQLite, or CSV upload. For
  relational engines, supply the host, port, database name, and credentials. AIInsights365.net encrypts
  credentials at rest using AES-256 and never stores plaintext secrets in the application database.
</p>
<p>
  After saving, the platform runs a three-phase <em>connection lifecycle</em>. First, it validates
  network reachability and authentication. Second, it introspects the schema &mdash; discovering every
  table, view, column, data type, primary key, foreign key, and index &mdash; and caches this metadata
  to power agent schema injection. Third, it generates a sample preview of the first 20 rows from the
  ten largest tables so you can confirm the right database is connected before sharing it with your team.
  A green &ldquo;Ready&rdquo; badge on the datasource card confirms it is live and available to agents.
</p>

<h2>Step 2: Creating an AI Agent</h2>
<p>
  Agents are the conversational personas that users interact with. Each agent is bound to exactly one
  datasource and carries a customizable system prompt that shapes how the AI interprets questions and
  formats answers. Go to <strong>Agents &rarr; New Agent</strong>, select your datasource, and give the
  agent a descriptive name such as &ldquo;Sales Analyst&rdquo; or &ldquo;Finance Bot&rdquo;.
</p>
<p>
  The system prompt is the most powerful configuration lever available. Use it to define metric
  conventions (&ldquo;revenue always means net revenue after discounts&rdquo;), fiscal calendar rules
  (&ldquo;our fiscal year starts July 1&rdquo;), excluded tables (&ldquo;never query the
  <code>audit_log</code> table&rdquo;), and preferred chart types for common question patterns. The
  AIInsights365.net engine automatically appends a compressed schema snapshot to every system prompt at
  runtime, so you never need to paste column names manually. Save the agent and it is immediately
  available in the workspace chat panel.
</p>

<h2>Step 3: Using Ask AI</h2>
<p>
  Open a workspace and select your new agent from the agent drop-down in the chat header. Type your first
  question in plain English. Good first questions are concrete and specific: &ldquo;What were our top ten
  products by revenue last quarter?&rdquo; or &ldquo;Show me daily active users for the past 30 days as
  a line chart.&rdquo; The AIInsights365.net reasoning engine will parse your intent, generate a SQL
  query, execute it, pick the right visualization, and stream the complete answer back to your browser
  &mdash; typically in two to five seconds depending on datasource response time.
</p>
<p>
  Below each answer you will find the <strong>Reasoning Panel</strong>: a collapsible section showing
  the exact SQL that was executed, the column-to-measure mappings the model applied, and a brief
  plain-English rationale for the chart type chosen. Click the <strong>Pin</strong> button to add the
  chart or table to any dashboard in your organization. Follow-up questions inherit the full conversation
  context &mdash; you can refine filters, change time ranges, or switch visualizations without starting
  a new chat. The <strong>floating Q&amp;A panel</strong> is also accessible from any saved report or
  dashboard, letting you ask contextual questions without leaving the artifact view.
</p>

<h2>Step 4: Configuring User Settings</h2>
<p>
  Every AIInsights365.net user has a personal <strong>Settings</strong> page accessible from the avatar
  menu in the top-right corner. The Profile tab lets you update your display name, avatar image, and
  email address. The Security tab is where you change your password and enable
  <strong>two-factor authentication</strong> using any TOTP-compatible app such as Google Authenticator,
  Authy, or 1Password. Scanning the displayed QR code enrolls your device; subsequent logins require both
  your password and a six-digit one-time code. We strongly recommend enabling 2FA for all accounts that
  have Admin or Editor roles.
</p>
<p>
  The Notifications tab lets you control email digests for report completions, dashboard share
  notifications, and token-budget alerts. These settings are personal and do not affect organization-wide
  notification policies, which are configured separately by the organization administrator in the Admin
  panel.
</p>

<h2>Step 5: Organization &amp; Access Control</h2>
<p>
  The <strong>Admin &rarr; Team</strong> section is where you invite colleagues and assign roles.
  AIInsights365.net implements four RBAC roles:
</p>
<table>
  <thead>
    <tr><th>Role</th><th>Ask AI</th><th>Pin &amp; Dashboard</th><th>Share</th><th>Admin</th></tr>
  </thead>
  <tbody>
    <tr><td>Viewer</td><td>No</td><td>View only</td><td>No</td><td>No</td></tr>
    <tr><td>Analyst</td><td>Yes</td><td>Personal dashboards</td><td>No</td><td>No</td></tr>
    <tr><td>Editor</td><td>Yes</td><td>Team dashboards</td><td>Yes</td><td>No</td></tr>
    <tr><td>Admin</td><td>Yes</td><td>Full</td><td>Yes</td><td>Yes</td></tr>
  </tbody>
</table>
<p>
  Invite a team member by entering their email address and selecting a role. They receive an invitation
  email with a secure one-time link that expires in 48 hours. Admins can change a user&rsquo;s role or
  revoke access at any time from the same Team page. Token quotas &mdash; the maximum number of AI tokens
  a user may consume per month &mdash; can be set at the plan level or overridden per individual user,
  giving administrators fine-grained cost control in large organizations.
</p>
<p>
  Subscription management is handled through the <strong>Admin &rarr; Billing</strong> page, which links
  to the PayPal checkout flow for plan upgrades, downgrades, and cancellations. All new organizations
  receive a <strong>30-day free trial</strong> of the Professional plan with no credit card required.
</p>

<blockquote>
  <strong>Best Practice:</strong> Create one agent per distinct business domain (sales, finance, operations)
  and assign the narrowest role that meets each user&rsquo;s needs. Use Viewer for executive stakeholders
  who only consume shared dashboards, and reserve Admin for a maximum of two or three trusted
  administrators in your organization. Review token usage weekly during onboarding to calibrate quotas
  before your trial ends.
</blockquote>

<h2>Next Steps</h2>
<p>
  Now that you have a datasource, agent, and your first pinned chart, explore the full
  <strong>AIInsights365.net</strong> chart library (72+ types across seven categories), build a branded
  dashboard for your executive team, and try the Auto Report Generator by clicking
  <strong>Reports &rarr; Generate</strong> and describing the report you need in plain English. The
  platform will draft titles, narrative sections, supporting charts, and a table of contents automatically.
</p>
"""
            },
            new DocArticle
            {
                Title       = "Chart Library \u2014 72+ Visualizations &amp; AI-Driven Selection",
                Slug        = "chart-library-overview",
                Summary     = "A complete guide to the AIInsights365.net chart library: 72+ chart types across 7 categories, how AI picks the right visualization, and how the unified renderer handles large datasets.",
                Author      = "AIInsights365 Team",
                SortOrder   = 3,
                IsPublished = true,
                CreatedAt   = now,
                UpdatedAt   = now,
                Content     = """
<h2>The AIInsights365.net Chart Library</h2>
<p>
  Visualization is the final mile of analytics. A perfectly written SQL query that produces the wrong
  chart type leaves analysts confused and executives unconvinced. <strong>AIInsights365.net</strong>
  eliminates that risk with a chart library spanning 72+ distinct visualization types, organized
  into seven semantic categories, and powered by an AI selection engine that chooses the right chart for
  your question automatically. This document walks through every category, explains how the AI makes its
  selection, and covers the customization, export, and accessibility features available to every user.
</p>

<h2>The Seven Chart Categories</h2>

<h3>1. Comparison</h3>
<p>
  Comparison charts are the workhorse of business analytics. The AIInsights365.net comparison category
  includes vertical <strong>bar charts</strong>, horizontal <strong>column charts</strong>,
  <strong>grouped bar</strong> charts for side-by-side category comparison,
  <strong>stacked bar</strong> and <strong>percent-stacked bar</strong> charts for part-to-whole
  comparisons within a category, and <strong>lollipop charts</strong> for ranked lists where minimizing
  ink-to-data ratio matters. The AI selects a comparison chart whenever your question contains words such
  as &ldquo;compare,&rdquo; &ldquo;rank,&rdquo; &ldquo;top N,&rdquo; or &ldquo;which is largest.&rdquo;
</p>

<h3>2. Trend</h3>
<p>
  Trend charts reveal how a metric evolves over time. The category includes classic <strong>line
  charts</strong>, filled <strong>area charts</strong>, <strong>smooth-line</strong> and
  <strong>spline</strong> variants for interpolated curves, <strong>step line</strong> charts for metrics
  that change discretely (such as headcount), <strong>range area</strong> charts for confidence intervals
  or min-max bands, and <strong>candlestick</strong> charts for financial OHLC data. When a question
  contains a time dimension and a single measure, the AI defaults to a line chart. When multiple measures
  share the same time axis, it selects an area chart to convey relative magnitude.
</p>

<h3>3. Composition</h3>
<p>
  Composition charts show how a whole is divided into parts. <strong>Pie</strong> and <strong>donut</strong>
  charts are best for five or fewer categories. <strong>Sunburst</strong> charts extend the donut concept
  to two or three levels of hierarchy, making them ideal for product-category breakdowns.
  <strong>Treemaps</strong> use nested rectangles to display hierarchical data with a quantitative
  dimension encoded as area, making them excellent for budget or revenue breakdowns across dozens of
  categories. <strong>Waterfall</strong> charts visualize running totals with positive and negative
  contributions, a staple of financial bridge analyses. <strong>Marimekko</strong> (mosaic) charts encode
  both category share and segment proportion simultaneously in a two-dimensional grid.
</p>

<h3>4. Distribution</h3>
<p>
  Distribution charts reveal the shape of a dataset. <strong>Histograms</strong> bucket continuous values
  into frequency bins. <strong>Box plots</strong> summarize a distribution through its quartiles and
  outliers, enabling rapid comparison across groups. <strong>Violin plots</strong> add kernel density
  estimation on top of the box-plot structure for a richer statistical view. <strong>Scatter plots</strong>
  plot two continuous variables against each other to reveal correlations or clusters.
  <strong>Bubble charts</strong> add a third dimension via marker size. <strong>Density plots</strong>
  (KDE curves) smooth out histogram bins to reveal underlying distributions without the arbitrary choice
  of bin width.
</p>

<h3>5. Flow</h3>
<p>
  Flow charts are designed for data that moves through a system. <strong>Sankey diagrams</strong> visualize
  flows between nodes where the width of each link encodes the flow magnitude &mdash; perfect for customer
  journey funnels, energy flows, and budget allocations. <strong>Chord diagrams</strong> show bidirectional
  relationships between a set of entities in a circular layout. <strong>Network graphs</strong> render
  arbitrary node-edge structures, useful for organizational hierarchies, dependency maps, and social
  networks. <strong>Funnel charts</strong> display progressive reductions through a process pipeline, and
  <strong>parallel coordinates</strong> plots display multi-dimensional data as polylines crossing parallel
  axes, a powerful tool for exploring high-dimensional feature spaces.
</p>

<h3>6. Geospatial</h3>
<p>
  Geospatial charts bring location intelligence to AIInsights365.net. <strong>Choropleth maps</strong>
  shade regions (countries, states, counties) according to a quantitative measure, ideal for regional
  sales or demographic data. <strong>Bubble maps</strong> overlay proportional circles on geographic
  coordinates to show concentration. <strong>Point cluster maps</strong> aggregate dense point datasets
  into clusters that expand on zoom. <strong>Heatmap overlays</strong> render continuous geographic
  distributions using a color gradient, commonly used for foot traffic, delivery density, and signal
  strength analysis. Geospatial charts render on an interactive Mapbox base layer with zoom, pan, and
  tooltip support.
</p>

<h3>7. KPI</h3>
<p>
  KPI charts present headline numbers with context. <strong>Gauge charts</strong> display a single value
  against a target range using a semicircular dial. <strong>Bullet charts</strong> show a primary measure,
  a comparative measure, and qualitative bands (poor, satisfactory, good) in a compact horizontal bar.
  <strong>Scorecard tiles</strong> display a large primary metric with trend arrow and delta percentage,
  the most common tile type on executive dashboards. <strong>Sparklines</strong> embed a thumbnail trend
  line inside a scorecard for temporal context without a full chart. <strong>Progress rings</strong>
  display percentage completion in a circular arc, used for quota attainment and project milestones.
</p>

<h2>AI-Driven Chart Selection</h2>
<p>
  When you ask a question in <strong>AIInsights365.net</strong>, the conversational engine analyses the
  semantic intent of your question, the number of dimensions, the number of measures, the cardinality of
  categorical fields, and whether a time dimension is present. It then maps this analysis to a chart type
  using a learned classification model fine-tuned on tens of thousands of question-to-chart pairs. For
  example: a question containing &ldquo;trend&rdquo; or &ldquo;over time&rdquo; with a single measure
  maps to a line chart; &ldquo;breakdown&rdquo; or &ldquo;share of&rdquo; with fewer than six categories
  maps to a donut chart; &ldquo;distribution of&rdquo; maps to a histogram or box plot depending on
  whether the question implies grouping.
</p>
<p>
  You can override the AI recommendation at any time by opening the <strong>Chart Properties</strong>
  panel (the palette icon above every chart) and selecting any of the 72 types from a categorized
  picker. A &ldquo;Quick Swap&rdquo; button cycles through the top three AI-recommended alternatives
  without opening the full panel, making it fast to try a bar chart versus a column chart or a donut
  versus a treemap.
</p>

<h2>The Unified Renderer</h2>
<p>
  All 72 chart types in AIInsights365.net share a single canvas-based rendering pipeline. This means
  consistent font rendering, color theming, and interaction patterns regardless of chart type. The
  renderer uses a virtual scrolling strategy for tabular data and switches to WebGL acceleration
  automatically when dataset row counts exceed 50,000, enabling smooth pan-and-zoom interactions on
  scatter plots and maps with millions of data points. Lazy loading and progressive rendering ensure
  that dashboards with many tiles do not block the main thread during load.
</p>

<h2>Customization</h2>
<p>
  Every chart exposes a rich set of customization options: color palette selection (from organization
  brand palettes or custom hex codes), axis label formatting (number format, date format, custom prefix
  and suffix), tooltip templates, legend position and visibility, reference lines and bands (targets,
  thresholds, averages), and annotation markers for significant events. Customizations are saved per tile
  on a dashboard and are independent of the underlying query, so the same dataset can be displayed
  differently on different dashboards.
</p>

<h2>Exporting Charts</h2>
<p>
  Any chart can be exported as a <strong>PNG</strong> image (high-resolution, suitable for presentations),
  an <strong>SVG</strong> vector graphic (infinite scale, ideal for print), or a <strong>CSV</strong>
  data download of the underlying query result. Export options are available from the chart context menu
  (three-dot icon). Bulk export of an entire dashboard is available from the dashboard header menu and
  packages all tiles into a single ZIP archive.
</p>

<h2>Accessibility</h2>
<p>
  <strong>AIInsights365.net</strong> is committed to accessible data visualization. Every chart rendered
  by the platform includes ARIA role and label attributes so screen readers can announce chart type,
  title, and axis descriptions. Keyboard navigation is supported for all interactive chart elements.
  When a chart is generated by the Ask AI engine, the platform also produces an AI-authored plain-text
  description of the chart&rsquo;s key findings, which is exposed as the chart&rsquo;s ARIA description
  and displayed in a collapsible &ldquo;Data Narrative&rdquo; section below the chart for all users.
</p>
"""
            },
            new DocArticle
            {
                Title       = "Reports, Dashboards &amp; the AI Q&amp;A Panel",
                Slug        = "reports-dashboards-qa-panel",
                Summary     = "How AIInsights365.net auto-generates reports, lets you build dashboards by pinning chat results, and keeps every artifact alive with a draggable AI Q&amp;A panel — plus hosting and sharing notes.",
                Author      = "AIInsights365 Team",
                SortOrder   = 4,
                IsPublished = true,
                CreatedAt   = now,
                UpdatedAt   = now,
                Content     = """
<h2>From Chat Result to Shareable Artifact</h2>
<p>
  In legacy BI tools, creating a report or dashboard is a separate, time-consuming authoring task.
  In <strong>AIInsights365.net</strong>, every chat response is already a publishable artifact.
  One click pins it to a dashboard. One prompt generates a full multi-page report. And every artifact
  &mdash; whether a simple bar chart or a twenty-page executive report &mdash; carries a live AI
  assistant that stakeholders can interrogate without returning to the chat interface. This document
  explains the complete pipeline from chat result to polished, shared deliverable.
</p>

<h2>Pinning Chat Results as Dashboard Tiles</h2>
<p>
  Every Ask AI response includes a <strong>Pin</strong> button in the top-right corner of the result
  card. Clicking it opens a modal that lets you select a target dashboard (or create a new one) and
  optionally rename the tile. The tile captures the chart type, customization settings, and the
  underlying query at the moment of pinning. Tile types include: <em>Chart</em> (any of the 72
  visualization types), <em>Table</em> (paginated tabular result), <em>KPI</em> (scorecard with
  trend delta), and <em>Text</em> (AI-generated narrative pinned as a rich-text block).
</p>
<p>
  Tiles are live &mdash; they re-execute the underlying query on each dashboard refresh (configurable:
  on-load, every 15 minutes, every hour, or manual). This means pinned tiles always reflect current
  data without any manual republishing step.
</p>

<h2>Dashboard Layout</h2>
<p>
  Dashboards in <strong>AIInsights365.net</strong> use a 12-column responsive grid. Tiles can span
  1 to 12 columns and 1 to 8 row units. Drag any tile by its header to reorder it; grab the
  resize handle in the bottom-right corner to change its dimensions. The layout engine snaps tiles
  to the grid and prevents overlaps automatically. Breakpoint configurations for tablet (768px) and
  mobile (375px) viewports are generated automatically from the desktop layout but can be customized
  independently, allowing you to hide low-priority tiles on small screens without removing them from
  the desktop view.
</p>

<h2>Dashboard Theming</h2>
<p>
  Organization administrators can define a brand theme in <strong>Admin &rarr; Branding</strong>.
  The theme includes: primary color, accent color, background color, chart palette (up to 12 colors
  applied in sequence across series), organization logo, and font family. All dashboards in the
  organization inherit the active theme by default. Individual dashboards can opt into an alternative
  theme (for example, a dark-mode executive theme) from the dashboard settings panel. Theme changes
  apply immediately to all live and shared views without requiring republishing.
</p>

<h2>Dashboard Filters</h2>
<p>
  Dashboard-level filters allow a viewer to adjust the scope of all tiles simultaneously. Available
  filter types include: <em>Date Range Picker</em> (a dual-calendar range selector that propagates
  a date interval parameter to all tile queries that reference a date dimension), <em>Dropdown
  Selector</em> (for categorical fields such as region, product category, or sales channel), and
  <em>Free-Text Search</em> (performs a parameterized LIKE filter on a designated text column).
  Administrators configure which fields are exposed as filters and which tiles they affect; not all
  tiles need to respond to every filter. Filter state is preserved in the URL, making it easy to
  bookmark or share a pre-filtered dashboard view.
</p>

<h2>Sharing Dashboards</h2>
<p>
  To share a dashboard externally, open <strong>Dashboard &rarr; Share</strong> and click
  <em>Generate Link</em>. The platform issues a cryptographically random token embedded in a URL.
  Recipients who open the link see a read-only view of the dashboard with live data but without access
  to any other part of the <strong>AIInsights365.net</strong> platform. No account is required.
  Share links can be configured with an expiry (1 day, 7 days, 30 days, or never) and can be revoked
  at any time from the Share management panel. For internal sharing, team members with Viewer roles or
  above can access any dashboard marked as <em>Team</em> visibility within their organization.
</p>

<h2>Auto Report Generator</h2>
<p>
  The Auto Report Generator transforms a plain-English prompt into a fully formatted multi-page
  report. Navigate to <strong>Reports &rarr; Generate</strong> and describe your report: &ldquo;Quarterly
  sales performance review for Q3, covering revenue by region, top products, customer acquisition
  cost trend, and an anomaly analysis of weeks where revenue dropped more than 10% week-over-week.&rdquo;
  The AIInsights365.net engine then: (1) decomposes the prompt into a set of data questions, (2)
  generates and executes a SQL query for each question, (3) selects the appropriate chart type for
  each result, (4) drafts a narrative paragraph interpreting each finding, and (5) assembles everything
  into a paginated report with a generated table of contents.
</p>
<p>
  Reports support multi-page layout with automatic page breaks, section headers, and a branded cover
  page. The generation process typically takes 15&ndash;45 seconds depending on the number of questions
  and datasource query time. Once generated, the report is saved to the Reports library and accessible
  to all team members with Analyst roles or above.
</p>

<h2>Report Revision History</h2>
<p>
  Every time a report is regenerated or manually edited, <strong>AIInsights365.net</strong> saves a
  snapshot to the revision history. The Revisions panel (accessible from the report header) shows a
  timeline of all snapshots with timestamps and author names. You can open any historical snapshot in
  a side-by-side comparison view to see what changed between versions, and restore any previous snapshot
  as the current report with a single click. Revision history is retained for 90 days on Professional
  plans and indefinitely on Enterprise plans.
</p>

<h2>The Floating Q&amp;A Panel</h2>
<p>
  Every saved report and every dashboard in <strong>AIInsights365.net</strong> includes a
  <strong>floating action button</strong> (FAB) in the bottom-right corner &mdash; a chat bubble icon
  that opens the AI Q&amp;A panel. The panel is draggable: click and hold the header bar to reposition
  it anywhere on the screen without leaving the current view. Inside the panel, users can ask any
  question in natural language, and the AI responds in the context of the current report or dashboard
  &mdash; aware of which charts are displayed and what data underlies them.
</p>
<p>
  Streaming responses appear word by word, just as in the main chat interface. Users can request
  alternate chart types (&ldquo;show the same data as a treemap instead&rdquo;), ask for anomaly
  explanations (&ldquo;why did revenue drop in week 32?&rdquo;), or export individual findings as
  images or CSV directly from the panel. The Q&amp;A panel is available to all users who have access
  to the report or dashboard, including external share-link recipients, providing a richer read-only
  experience than static PDF exports.
</p>

<h2>Hosting &amp; Deployment Topology</h2>
<p>
  The AIInsights365.net platform is deployed as three distinct ASP.NET Core applications behind a shared
  load balancer. The <strong>Web Application</strong> serves the user-facing React front end and API.
  The <strong>Super-Admin Portal</strong> is a separate, restricted application for platform operators
  to manage organization lifecycle, plan limits, and support escalations. The <strong>Gateway App</strong>
  acts as a tenant-aware reverse proxy: it validates JWT tokens, enforces token budget accounting, applies
  rate limits per organization, and routes requests to the appropriate application instance.
</p>
<p>
  All three applications are stateless &mdash; session state is stored in a distributed Redis cache,
  not in server memory &mdash; enabling horizontal scaling with zero-downtime rolling deployments.
  The multi-tenant SQL Server backend uses row-level security views and schema-level isolation to prevent
  data leakage between tenants. TLS 1.3 is enforced at the load balancer. Configuration is externalized
  through Azure App Configuration and Azure Key Vault, so deployments to staging and production differ
  only in environment-specific settings, not in application code.
</p>

<h2>Conclusion</h2>
<p>
  <strong>AIInsights365.net</strong> collapses the gap between asking a data question and sharing a
  polished answer. By making every chat response a potential dashboard tile, every prompt a potential
  report, and every artifact a live AI conversation partner, the platform delivers a fundamentally
  different analytics experience &mdash; one where insights are never more than a sentence away.
</p>
"""
            }
        };

        int inserted = 0, updated = 0;
        foreach (var d in docs)
        {
            var existing = await _db.DocArticles.FirstOrDefaultAsync(x => x.Slug == d.Slug);
            if (existing == null)
            {
                _db.DocArticles.Add(d);
                inserted++;
            }
            else
            {
                existing.Title       = d.Title;
                existing.Summary     = d.Summary;
                existing.Content     = d.Content;
                existing.Author      = d.Author;
                existing.SortOrder   = d.SortOrder;
                existing.IsPublished = d.IsPublished;
                existing.UpdatedAt   = now;
                updated++;
            }
            await EnsureSeoAsync(
                pageUrl:    $"/docs/{d.Slug}",
                title:      $"{d.Title} \u2014 AIInsights365.net",
                description: d.Summary,
                keywords:   "AIInsights365, AI analytics, documentation, AI insights, data AI, " + d.Slug.Replace('-', ' '),
                priority:   0.7m,
                changeFreq: "monthly");
        }
        await _db.SaveChangesAsync();
        _log.LogInformation("ContentSeeder: inserted {Inserted} / updated {Updated} DocArticle(s).", inserted, updated);
    }

    // ───────────────────────────── Blog ─────────────────────────────
    private async Task SeedBlogAsync()
    {
        var now   = DateTime.UtcNow;
        var posts = new[]
        {
            new BlogPost
            {
                Title       = "Introducing AIInsights365.net \u2014 Phase 1 Is Live &amp; Phase 2 Is Coming",
                Slug        = "introducing-aiinsights365-phase-1",
                Summary     = "Everything that shipped in Phase 1 — AI chat, 72+ charts, agent workspaces, dashboards, and an enterprise user layer — plus a first look at Phase 2: TOML-based configuration and AI Insight ETL with DAX queries.",
                Author      = "AIInsights365 Team",
                PublishedAt = DateTime.UtcNow.AddDays(-6),
                IsPublished = true,
                Content     = """
<h2>Phase 1 Is Live: AIInsights365.net Is Open for Business</h2>
<p>
  Today we are delighted to announce that <strong>AIInsights365.net Phase 1</strong> is generally
  available. After eighteen months of design, engineering, and closed-beta testing with more than two
  hundred organizations, we are confident that what we have built is unlike anything else in the
  analytics market: a platform where artificial intelligence is not a feature added to a BI tool,
  but the core architecture around which every capability is designed.
</p>

<h2>What Phase 1 Delivers</h2>
<p>Phase 1 ships the following major capability areas:</p>
<ul>
  <li><strong>Datasource Connectivity</strong> &mdash; SQL Server, PostgreSQL, MySQL, SQLite, and CSV, with schema introspection, credential encryption, and sample previews.</li>
  <li><strong>Ask AI</strong> &mdash; Natural-language to SQL, streaming answers, a transparent Reasoning Panel with cited SQL, and token-budgeted conversation management.</li>
  <li><strong>AI Agents</strong> &mdash; Configurable AI personas scoped to a datasource with custom system prompts, auto schema injection, and multi-agent support per organization.</li>
  <li><strong>Chart Library</strong> &mdash; 72+ visualization types across seven categories, with AI-driven chart selection and a unified WebGL-accelerated renderer.</li>
  <li><strong>Dashboards</strong> &mdash; One-click pinning of chat results, drag-and-resize tile layouts, org branding themes, dashboard-level filters, and tokenized share links.</li>
  <li><strong>Hosting Topology</strong> &mdash; Stateless web app, Super-Admin portal, and Gateway app; multi-tenant SQL Server backend; horizontal scaling ready.</li>
  <li><strong>User Settings &amp; RBAC</strong> &mdash; Profile management, TOTP-based 2FA, four-tier RBAC (Viewer/Analyst/Editor/Admin), PayPal subscriptions, and a 30-day free trial.</li>
</ul>
<p>
  Together these capabilities deliver on the promise we made when we founded the company: that every
  employee &mdash; not just data engineers or SQL experts &mdash; should be able to get precise,
  trustworthy answers from their organization&rsquo;s data in seconds. In our beta, organizations
  reported a 60% reduction in ad-hoc data requests to their analytics teams within the first month
  of using <strong>AIInsights365.net</strong>.
</p>
<p>
  Building Phase 1 taught us that AI-first architecture is fundamentally different from adding AI to
  an existing product. Schema introspection at datasource-save time, not at query time, is what makes
  agent responses fast and accurate. Streaming responses are not a nice-to-have; they are the mechanism
  by which users trust that the AI is genuinely working rather than displaying a cached result. And
  transparency &mdash; showing the SQL, the reasoning, the chart-selection rationale &mdash; is what
  converts skeptical analysts into platform champions.
</p>

<h2>A First Look at Phase 2</h2>
<p>
  While Phase 1 was in closed beta, our engineering team began Phase 2. Two capabilities define it.
</p>

<h3>TOML-Based Configuration</h3>
<p>
  TOML (Tom&rsquo;s Obvious Minimal Language) is a configuration format designed to be human-friendly,
  semantically unambiguous, and easy to diff in a pull request. In Phase 2, datasource connections,
  agent definitions, and ETL job schedules will be expressible as TOML files that can live in version
  control alongside application code. Here is an example datasource block:
</p>
<pre><code class='language-toml'>[datasource.sales_db]
type     = "sqlserver"
host     = "sql.internal.example.com"
port     = 1433
database = "SalesAnalytics"
schema   = "dbo"

[datasource.sales_db.auth]
method   = "sql"
username = "svc_aiinsights"
password_env = "SALES_DB_PASSWORD"
</code></pre>
<p>
  Compare this to a JSON equivalent with no comments, no support for trailing commas, and no way to
  annotate fields inline. TOML makes datasource configuration reviewable, auditable, and onboarding-
  friendly in a way JSON simply cannot match.
</p>

<h3>AI Insight ETL with DAX</h3>
<p>
  The second Phase 2 flagship is an ETL pipeline that uses DAX (Data Analysis Expressions) queries to
  extract curated business metrics from tabular models &mdash; including SQL Server Analysis Services,
  Azure Analysis Services, and Power BI Premium XMLA endpoints &mdash; and load them into the
  AIInsights365.net <em>insight store</em>. AI agents then reason over pre-computed, semantically named
  measures rather than raw relational tables, making answers more accurate and token-efficient.
  We will publish a full deep dive on the DAX ETL pipeline in the coming weeks.
</p>

<h2>Get Started Today</h2>
<p>
  <strong>AIInsights365.net</strong> is available now with a 30-day free trial. Connect your first
  datasource, spin up an AI agent, and ask your data a question in plain English. We think you will
  find the experience unlike any analytics tool you have used before. Visit
  <a href='https://aiinsights365.net'>aiinsights365.net</a> to create your organization account.
</p>
"""
            },
            new BlogPost
            {
                Title       = "Phase 2 Roadmap \u2014 TOML-Configured Datasources &amp; AI Insight ETL",
                Slug        = "phase-2-roadmap-15-datasources",
                Summary     = "Phase 2 introduces TOML as a human-friendly configuration format for datasources, agents, and ETL jobs, plus an AI Insight ETL pipeline that uses DAX queries to feed curated metrics to your AI agents.",
                Author      = "AIInsights365 Team",
                PublishedAt = DateTime.UtcNow.AddDays(-4),
                IsPublished = true,
                Content     = """
<h2>Phase 2: Two Pillars, Fifteen Connectors</h2>
<p>
  Phase 2 of <strong>AIInsights365.net</strong> is organized around two architectural pillars: a
  <strong>TOML-based configuration system</strong> that makes datasource, agent, and ETL job
  definitions version-controllable and human-readable, and an <strong>AI Insight ETL pipeline</strong>
  that uses DAX queries to extract curated semantic metrics from tabular models and load them into a
  structured insight store. Layered on top of both pillars is a dramatic expansion of the connector
  catalog to fifteen production-grade datasources.
</p>

<h2>What Is TOML?</h2>
<p>
  TOML stands for Tom&rsquo;s Obvious Minimal Language. It was designed by Tom Preston-Werner
  (co-founder of GitHub) as a configuration format that is semantically unambiguous, easy for humans
  to read and write, and easy for machines to parse. Unlike JSON, TOML supports inline comments (lines
  beginning with <code>#</code>) and avoids the error-prone trailing-comma rules that trip up junior
  engineers. Unlike YAML, TOML uses explicit typing (strings are always quoted, integers are never
  ambiguous, datetimes are RFC 3339 values) and does not rely on indentation for structure, eliminating
  the &ldquo;two spaces vs four spaces&rdquo; class of bugs. Unlike INI files, TOML supports nested
  sections and typed arrays, making it expressive enough for complex configuration without becoming a
  mini-programming-language.
</p>
<p>
  In a CI/CD world where configuration changes are reviewed in pull requests, TOML&rsquo;s line-by-line
  readability produces clean diffs that any engineer can approve without deep domain knowledge. Switching
  a datasource from one host to another produces exactly one changed line. Adding a new agent produces
  one new <code>[[agent]]</code> block. These are the kinds of review interactions that build confidence
  and reduce misconfiguration incidents.
</p>

<h2>TOML in AIInsights365.net Phase 2</h2>
<p>
  Phase 2 introduces TOML configuration for three object types: datasources, agents, and ETL jobs.
  Each type has its own section syntax. Here are representative examples.
</p>

<h3>Datasource Block</h3>
<pre><code class='language-toml'># Snowflake analytics warehouse
[datasource.analytics_warehouse]
type     = "snowflake"
account  = "myorg.us-east-1"
database = "ANALYTICS"
schema   = "PUBLIC"
warehouse = "COMPUTE_WH"
role     = "AIINSIGHTS_ROLE"

[datasource.analytics_warehouse.auth]
method           = "key_pair"
username         = "svc_aiinsights"
private_key_path = "/run/secrets/snowflake_rsa.p8"
</code></pre>

<h3>Agent Block</h3>
<pre><code class='language-toml'>[[agent]]
name       = "Finance Analyst"
datasource = "analytics_warehouse"
model      = "gpt-4o"
token_budget = 50000

system_prompt = &quot;&quot;&quot;
You are a financial analyst assistant for Acme Corp.
Revenue always means net revenue after discounts and returns.
Our fiscal year starts July 1. Never query staging_ prefixed tables.
&quot;&quot;&quot;
</code></pre>

<h3>ETL Job Block</h3>
<pre><code class='language-toml'>[etl.daily_kpis]
schedule   = "0 3 * * *"        # 03:00 UTC every day
source     = "analytics_warehouse"
engine     = "dax"
output     = "insight_store"

[etl.daily_kpis.measures]
include = [
  "Total Revenue",
  "Revenue YTD",
  "Revenue YoY %",
  "Gross Margin %",
  "Customer Count",
  "Average Order Value",
]
</code></pre>

<h2>Why TOML for Datasource Config?</h2>
<p>
  Configuration files for database connections are frequently the first thing a new team member must
  touch when onboarding. JSON forces them to hunt for missing commas. YAML silently converts
  <code>ON</code> to boolean <code>true</code> and port numbers that look like octal to decimals.
  TOML makes the right thing obvious: strings are quoted, numbers are numbers, and the structure is
  immediately clear from the section headers. When the same file is committed to a Git repository, code
  reviewers can see exactly what changed without needing to understand the full system. At
  <strong>AIInsights365.net</strong> we believe the operational burden of managing many datasources
  and agents should scale sub-linearly with team size, and TOML configuration is a key part of that bet.
</p>

<h2>Phase 2 Connector Lineup</h2>
<p>
  Phase 2 expands the AIInsights365.net connector catalog from five to fifteen production-grade sources:
</p>
<ol>
  <li>Snowflake</li>
  <li>Google BigQuery</li>
  <li>Databricks (Delta Lake &amp; Unity Catalog)</li>
  <li>Amazon Redshift</li>
  <li>Oracle Database 19c+</li>
  <li>SAP HANA</li>
  <li>MongoDB Atlas</li>
  <li>Elasticsearch / OpenSearch</li>
  <li>ClickHouse</li>
  <li>Salesforce (SOQL)</li>
  <li>HubSpot (CRM API)</li>
  <li>Google Analytics 4</li>
  <li>Stripe (payments &amp; billing)</li>
  <li>Shopify (orders &amp; products)</li>
  <li>Generic REST / GraphQL endpoints</li>
</ol>
<p>
  Each connector ships with a metadata importer that maps the source&rsquo;s native schema concepts
  &mdash; Salesforce objects, GA4 dimensions and metrics, Stripe resources &mdash; into the
  AIInsights365.net unified schema model. Agents trained on these connectors can answer questions
  across heterogeneous sources in the same natural-language conversation, making cross-system analysis
  available to any analyst without SQL expertise.
</p>

<h2>AI Insight ETL Overview</h2>
<p>
  The AI Insight ETL pipeline is Phase 2&rsquo;s second major capability. It exists to solve a
  fundamental problem with raw SQL-based AI analytics: when an agent must reason from raw relational
  tables, it reconstructs business logic from scratch on every query &mdash; reinventing joins,
  recalculating year-to-date measures, and approximating fiscal calendar logic. This is slow,
  token-expensive, and error-prone.
</p>
<p>
  The insight store is a read-optimized layer that holds pre-computed, semantically named business
  measures: &ldquo;Total Revenue,&rdquo; &ldquo;Revenue YTD,&rdquo; &ldquo;Gross Margin %,&rdquo;
  &ldquo;Customer Retention Rate.&rdquo; When an AI agent answers &ldquo;What is our YTD revenue by
  region?&rdquo; it retrieves a pre-calculated row from the insight store rather than generating a
  multi-join SQL query. The result is faster, more accurate, and consumes far fewer tokens.
</p>
<p>
  The ETL pipeline that populates the insight store uses DAX queries against tabular models, because
  DAX already encodes the business logic (time intelligence, fiscal calendars, hierarchy navigation)
  that agents need. A single TOML-configured ETL job, scheduled via cron syntax and managed from the
  <strong>AIInsights365.net</strong> admin panel, keeps the insight store current. Phase 2 documentation
  will include a full walkthrough of the DAX ETL pipeline, the insight store schema, and best practices
  for measure naming.
</p>

<h2>What Comes After Phase 2?</h2>
<p>
  The Phase 2 roadmap is ambitious but focused. After shipping TOML configuration, the fifteen-connector
  lineup, and the DAX Insight ETL pipeline, Phase 3 plans to introduce collaborative workspaces,
  real-time collaboration on reports (similar to Google Docs), and a public-facing API for embedding
  AIInsights365.net visualizations in third-party applications. Subscribe to the blog or follow
  us on LinkedIn to stay up to date as we ship.
</p>
"""
            },
            new BlogPost
            {
                Title       = "AI Insight ETL with DAX \u2014 From Tabular Model to AI Agent",
                Slug        = "on-prem-data-gateway-phase-2",
                Summary     = "Phase 2 ships an AI Insight ETL pipeline that uses DAX queries to extract time-intelligence metrics from tabular models, transform them, and load them into the AIInsights365.net insight store where AI agents can reason over curated business measures.",
                Author      = "AIInsights365 Team",
                PublishedAt = DateTime.UtcNow.AddDays(-2),
                IsPublished = true,
                Content     = """
<h2>From Tabular Model to AI Agent: The DAX ETL Concept</h2>
<p>
  One of the most persistent challenges in AI-powered analytics is the gap between how business metrics
  are <em>defined</em> and how AI agents <em>retrieve</em> them. A data warehouse might store raw
  transaction records, but the metric &ldquo;Revenue Year-to-Date&rdquo; requires joining fact and
  dimension tables, applying a fiscal calendar filter, excluding certain transaction types, and summing
  the result. When an AI agent reconstructs this logic from scratch on every user query, it is slow,
  token-expensive, and prone to subtle errors if the schema is complex.
</p>
<p>
  Phase 2 of <strong>AIInsights365.net</strong> solves this with an <strong>AI Insight ETL
  pipeline</strong> that uses <strong>DAX queries</strong> to extract pre-computed, semantically named
  business measures from tabular models and load them into a structured insight store. This document
  explains what DAX is, why it is the right language for this ETL task, how the three-stage pipeline
  works, and how AI agents consume the insight store to deliver faster, more accurate answers.
</p>

<h2>What Is DAX?</h2>
<p>
  DAX &mdash; Data Analysis Expressions &mdash; is a formula and query language created by Microsoft for
  Power Pivot, SQL Server Analysis Services (SSAS) Tabular, Azure Analysis Services, and Power BI. It
  is a column-oriented, set-based language that operates on tables and relationships, similar in spirit
  to Excel formulas but designed for analytical datasets at scale.
</p>
<p>
  DAX has two distinct usage modes. As a <em>measure language</em>, DAX expressions define calculated
  metrics that are evaluated at query time in the context of whatever filters are active on the data
  model &mdash; a concept called <em>filter context</em>. As a <em>query language</em>, DAX uses the
  <code>EVALUATE</code> statement to return tabular rowsets, much like a SQL <code>SELECT</code>.
  The combination makes DAX uniquely powerful for ETL: you can write a DAX query that evaluates
  business measures across multiple dimension combinations in a single round-trip, returning a
  structured result set that encodes months of business logic in a few dozen lines.
</p>
<p>
  DAX is also the native language of Microsoft&rsquo;s time-intelligence functions:
  <code>DATESYTD</code>, <code>SAMEPERIODLASTYEAR</code>, <code>DATEADD</code>,
  <code>PARALLELPERIOD</code>, and dozens more. These functions are aware of fiscal calendars,
  work-day calendars, and custom date tables, and they produce correct results for complex time
  comparisons without requiring the developer to write bespoke SQL date arithmetic.
</p>

<h2>Why DAX as an ETL Language?</h2>
<p>
  The key advantage of using DAX for the AIInsights365.net ETL pipeline is that the business logic
  already lives in the tabular model. Large organizations invest years in building Power BI or SSAS
  models with carefully defined KPIs, certified measures, and approved fiscal calendar conventions.
  These measures encode the organization&rsquo;s &ldquo;single version of the truth.&rdquo; By using
  DAX to query the model rather than raw SQL to query the underlying warehouse, the ETL pipeline
  inherits that pre-existing, certified business logic without reimplementing it. There is no risk of
  the ETL pipeline computing Revenue YTD differently from the official Power BI report, because both
  use the same DAX measure definition evaluated by the same tabular engine.
</p>

<h2>The Three-Stage Pipeline</h2>

<h3>Stage 1: Extract</h3>
<p>
  The extract stage issues <code>EVALUATE SUMMARIZECOLUMNS</code> DAX queries against a tabular model
  via an XMLA endpoint (SQL Server Analysis Services, Azure Analysis Services, or Power BI Premium).
  Each ETL job defines a set of measures to extract and the dimension combinations to group by:
</p>
<pre><code class='language-dax'>EVALUATE
SUMMARIZECOLUMNS (
    'Date'[Year],
    'Date'[Month],
    'Date'[MonthName],
    'Geography'[Region],
    'Product'[Category],
    "Total Revenue",      [Total Revenue],
    "Revenue YTD",        [Revenue YTD],
    "Revenue YoY %",      [Revenue YoY %],
    "Gross Margin %",     [Gross Margin %],
    "Customer Count",     [Customer Count]
)
ORDER BY 'Date'[Year], 'Date'[Month], 'Geography'[Region]
</code></pre>
<p>
  This single query returns a complete tabular rowset covering every year-month-region-category
  combination, with all five measures evaluated correctly by the tabular engine. The AIInsights365.net
  ETL driver streams the result set into memory for the transform stage.
</p>

<h3>Stage 2: Transform</h3>
<p>
  The transform stage normalizes the raw DAX result set into the <strong>AIInsights365.net insight
  store schema</strong>. This involves: renaming measure columns to camelCase keys that match the
  insight store&rsquo;s naming convention, handling null values (replacing null measures with zero or
  a sentinel value based on the measure type), applying data type coercions (DAX <code>Currency</code>
  types become <code>decimal</code>; <code>DateTime</code> values are converted to UTC ISO 8601),
  and mapping dimension values to the insight store&rsquo;s canonical identifiers for regions,
  categories, and time periods.
</p>

<h3>Stage 3: Load</h3>
<p>
  The load stage performs a bulk upsert into the insight store using the target measure name, dimension
  combination, and ETL job timestamp as the composite natural key. The insight store is a
  read-optimized columnar table in SQL Server with appropriate indexes on the dimension and time
  columns that AI agents use most frequently in their queries. A full refresh of 36 months of monthly
  data across five measures and four dimension combinations typically completes in under 30 seconds.
</p>

<h2>DAX Measure Definitions</h2>
<p>
  For reference, here are representative DAX measure definitions that the ETL pipeline extracts from
  a typical tabular model:
</p>
<pre><code class='language-dax'>-- Year-to-date revenue using DAX time intelligence
Revenue YTD :=
CALCULATE (
    [Total Revenue],
    DATESYTD ( 'Date'[Date], "6/30" )   -- fiscal year ends June 30
)

-- Year-over-year percentage change
Revenue YoY % :=
VAR PriorYear =
    CALCULATE (
        [Total Revenue],
        SAMEPERIODLASTYEAR ( 'Date'[Date] )
    )
RETURN
    IF (
        NOT ISBLANK ( PriorYear ) && PriorYear &lt;&gt; 0,
        DIVIDE ( [Total Revenue] - PriorYear, PriorYear ),
        BLANK ()
    )
</code></pre>
<p>
  These measures encode the organization&rsquo;s fiscal calendar convention (year-end June 30) and
  handle edge cases such as blank prior-year values. When the ETL pipeline extracts these measures
  via DAX query, it inherits this logic exactly &mdash; no reimplementation required.
</p>

<h2>How AI Agents Consume the Insight Store</h2>
<p>
  Once the insight store is populated, AIInsights365.net agents see curated measures as first-class
  named entities rather than raw tables. When a user asks &ldquo;What is our YTD revenue by region
  this quarter compared to last year?&rdquo; the agent queries the insight store for
  &ldquo;Revenue YTD&rdquo; and &ldquo;Revenue YoY %&rdquo; filtered to the current quarter and
  grouped by region. It does not need to join six tables, apply fiscal calendar logic, or handle
  null-prior-year edge cases &mdash; all of that is already baked into the pre-computed measures.
  The result is a semantically correct answer delivered in one to two seconds instead of ten to
  fifteen seconds for a raw warehouse query.
</p>

<h2>On-Prem Gateway Integration</h2>
<p>
  Many enterprise tabular models run on-premises in SQL Server Analysis Services instances that are
  not exposed to the public internet. The Phase 2 <strong>On-Prem Data Gateway</strong> brokers DAX
  queries from the AIInsights365.net ETL pipeline to on-prem SSAS instances over an outbound-only
  TLS tunnel. No inbound firewall ports are required. The gateway is a lightweight Windows service
  (installable via MSI or PowerShell) that authenticates to the AIInsights365.net cloud, receives
  encrypted DAX query payloads, forwards them to the local SSAS instance, and streams the encrypted
  result sets back. Credentials and query payloads are decrypted only within the gateway process,
  using per-tenant key material that never leaves the on-prem environment.
</p>

<h2>TOML Configuration for DAX ETL Jobs</h2>
<p>
  Each DAX ETL job is defined in a TOML configuration file, making it easy to version-control,
  review, and audit. Here is an example ETL job block that configures a nightly DAX extraction:
</p>
<pre><code class='language-toml'>[etl.nightly_financials]
schedule   = "0 2 * * *"          # 02:00 UTC nightly
source     = "ssas_finance_model"  # references a datasource block
engine     = "dax"
output     = "insight_store"
timeout_seconds = 300

[etl.nightly_financials.measures]
include = [
  "Total Revenue",
  "Revenue YTD",
  "Revenue YoY %",
  "Gross Margin %",
  "Operating Expense",
  "EBITDA",
  "Customer Count",
  "Average Order Value",
]

[etl.nightly_financials.dimensions]
include = [
  "Date[Year]",
  "Date[Month]",
  "Geography[Region]",
  "Product[Category]",
]
</code></pre>

<h2>Conclusion</h2>
<p>
  The DAX ETL pipeline is one of the most technically distinctive features of
  <strong>AIInsights365.net Phase 2</strong>. By bridging the world of certified tabular model
  measures with the conversational AI interface our users love, it eliminates the accuracy and
  performance problems that plague raw SQL-based AI analytics. Organizations that have invested in
  Power BI or SSAS tabular models can now surface those investments as first-class AI agent knowledge
  &mdash; without rebuilding any business logic. We will publish detailed setup guides and best
  practices for measure naming conventions as Phase 2 approaches general availability.
</p>
"""
            },
            new BlogPost
            {
                Title       = "Why AIInsights365.net Is Truly AI-First \u2014 TOML, DAX ETL, and the Architecture of Intelligence",
                Slug        = "why-aiinsights365-ai-first",
                Summary     = "Legacy BI tools bolt AI on as a feature. AIInsights365.net is architected around AI from the ground up — and Phase 2's TOML configuration system and DAX-powered AI Insight ETL are the proof.",
                Author      = "AIInsights365 Team",
                PublishedAt = DateTime.UtcNow.AddDays(-1),
                IsPublished = true,
                Content     = """
<h2>What Does &lsquo;AI-First&rsquo; Actually Mean?</h2>
<p>
  The term &ldquo;AI-first&rdquo; has been overloaded to the point of meaninglessness. Every
  established BI vendor now claims an AI-first strategy, typically referring to a natural-language
  search bar grafted onto a dashboard-authoring interface that was designed in 2010. That is not
  AI-first. That is AI-added. <strong>AIInsights365.net</strong> was designed from a blank sheet
  of paper with a single constraint: every architectural decision must maximize the effectiveness
  of AI, not the convenience of the dashboard editor.
</p>
<p>
  In this post we explain what that constraint produces in practice, using Phase 1&rsquo;s shipped
  capabilities and Phase 2&rsquo;s two flagship features &mdash; TOML configuration and DAX Insight
  ETL &mdash; as concrete illustrations of AI-first thinking applied across the full platform stack.
</p>

<h2>The Phase 1 Foundation</h2>
<p>
  Phase 1 establishes the AI-first foundation that Phase 2 builds on. Schema introspection happens
  at datasource-save time, not at query time, so agents have immediate access to a complete metadata
  snapshot the moment a user opens a conversation. This single architectural choice &mdash; separating
  the introspection phase from the query phase &mdash; is what enables sub-two-second first-token
  response times for complex queries on schemas with hundreds of tables.
</p>
<p>
  The agent system prompt mechanism is a second AI-first design decision. Rather than a monolithic
  AI that tries to understand every schema generically, <strong>AIInsights365.net</strong> agents are
  intentionally scoped: each agent is trained on a single datasource, carries business-context
  annotations from its system prompt, and has a compressed schema injected at conversation start.
  This scoping produces dramatically more accurate answers than a general-purpose AI operating on
  ambiguous schema names. The 72-chart library, AI chart selection, streaming responses, token budgets,
  and the floating Q&amp;A panel are all downstream consequences of the same AI-first constraint: design
  every feature to maximize the AI&rsquo;s ability to produce trustworthy answers quickly.
</p>

<h2>Phase 2 Pillar 1: TOML Configuration</h2>
<p>
  TOML is Phase 2&rsquo;s answer to a question that almost no analytics vendor asks: how do we make it
  easy for engineering teams to review, audit, and iterate on analytics infrastructure configuration?
  JSON and YAML are both common answers, and both are flawed for different reasons. TOML is unambiguous,
  comment-friendly, and produces clean PR diffs. Here is an example showing a datasource and agent
  configured together in a single TOML file:
</p>
<pre><code class='language-toml'># Production Snowflake datasource
[datasource.prod_warehouse]
type      = "snowflake"
account   = "acmecorp.eu-west-1"
database  = "PROD_ANALYTICS"
schema    = "BUSINESS"
warehouse = "QUERY_WH"

[datasource.prod_warehouse.auth]
method       = "key_pair"
username     = "svc_aiinsights"
private_key_path = "/run/secrets/sf_key.p8"

# Finance agent bound to the production warehouse
[[agent]]
name         = "Finance Agent"
datasource   = "prod_warehouse"
model        = "gpt-4o"
token_budget = 80000

system_prompt = &quot;&quot;&quot;
Financial analyst for Acme Corp European division.
Fiscal year ends December 31. Revenue = net after discounts.
Do not access staging_ or _temp tables.
Prefer EBITDA over gross profit when both are available.
&quot;&quot;&quot;
</code></pre>
<p>
  The human-readability of this configuration is not a cosmetic benefit. When a new engineer joins the
  team and must understand why the Finance Agent behaves differently from the Sales Agent, the TOML
  file answers that question in seconds. No web UI scraping, no undocumented API calls, no
  &ldquo;tribal knowledge.&rdquo; Configuration as code is an AI-first principle because it reduces
  the operational burden of running a complex AI platform, which in turn enables teams to scale their
  use of <strong>AIInsights365.net</strong> without proportional growth in administrative overhead.
</p>

<h2>Phase 2 Pillar 2: DAX Insight ETL</h2>
<p>
  The DAX Insight ETL pipeline is the purest expression of AI-first thinking in Phase 2. Its premise
  is simple: AI agents answer questions better when they reason over pre-computed, semantically named
  measures rather than raw relational tables. The pipeline uses DAX queries against tabular models
  to extract certified business metrics and load them into an insight store that agents consume at
  response time. Consider this DAX measure definition, which a typical Power BI or SSAS model already
  contains:
</p>
<pre><code class='language-dax'>Revenue YoY % :=
VAR PriorYear =
    CALCULATE (
        [Total Revenue],
        SAMEPERIODLASTYEAR ( 'Date'[Date] )
    )
RETURN
    IF (
        NOT ISBLANK ( PriorYear ) && PriorYear &lt;&gt; 0,
        DIVIDE ( [Total Revenue] - PriorYear, PriorYear ),
        BLANK ()
    )
</code></pre>
<p>
  When an AI agent answers &ldquo;How did revenue grow year-over-year last quarter?&rdquo; using the
  insight store, it retrieves a pre-calculated <code>Revenue YoY %</code> value that was computed by
  this exact measure definition. It does not need to understand the <code>SAMEPERIODLASTYEAR</code>
  function, the fiscal calendar, or the null-handling edge cases. The answer is already correct in the
  insight store. The agent&rsquo;s job is to retrieve it, format it, and explain it &mdash; which takes
  milliseconds and consumes a fraction of the tokens that a raw SQL reconstruction would require.
</p>

<h2>The Synergy: A Self-Improving Knowledge Loop</h2>
<p>
  The deepest insight in the AIInsights365.net Phase 2 architecture is the synergy between TOML and
  DAX ETL. TOML configures the ETL jobs that populate the insight store. The insight store is what
  AI agents query to answer user questions with precision. As organizations add new measures to their
  tabular models and update their TOML ETL job configurations to include those measures, the insight
  store grows. As the insight store grows, AI agents can answer a wider range of questions correctly
  without any changes to the agent system prompts or the AIInsights365.net application code.
</p>
<p>
  This is a self-improving knowledge loop: business analysts add measures in Power BI, a developer
  adds three lines to a TOML file, the nightly ETL job picks up the new measures, and by morning
  the AI agent can answer questions about those measures that it could not answer yesterday. The
  platform grows smarter as the organization&rsquo;s data model grows richer, with zero AI model
  retraining required.
</p>

<h2>Token Economics: The Competitive Moat</h2>
<p>
  Pre-computed insight store measures are not just semantically better &mdash; they are dramatically
  more token-efficient. A raw SQL AI answer for &ldquo;Revenue YTD by region this fiscal year vs last
  year&rdquo; might require 800-1,200 tokens in the prompt (schema context, join instructions, date
  logic) and produce a 500-token SQL query that the model must verify. The same question answered from
  the insight store requires 150-200 tokens (measure lookup, dimension filter, formatting instructions)
  and no generated SQL at all. At enterprise scale, this 5-to-8&times; token reduction translates
  directly to lower AI costs and higher token quota availability for complex exploratory questions.
</p>

<h2>The AI-First Competitive Advantage</h2>
<p>
  Legacy BI vendors are redesigning their UX around AI assistants. But UX is not architecture.
  <strong>AIInsights365.net</strong> is differentiated at the infrastructure level: schema introspection
  as a first-class pipeline step, scoped agents as the unit of AI deployment, a semantic insight store
  as the data layer, and TOML as the configuration backbone. These are not features. They are
  architectural commitments that make every subsequent capability more powerful, more accurate, and
  more cost-efficient than the equivalent capability built on a dashboard-first foundation.
</p>
<p>
  We invite you to experience the difference at <a href='https://aiinsights365.net'>AIInsights365.net</a>.
  Start a free trial, connect your first datasource, and ask your data a question in plain English.
  We are confident that the speed, accuracy, and transparency of the answers will demonstrate AI-first
  architecture in action &mdash; not as a marketing claim, but as a measurable user experience.
</p>
"""
            }
        };

        int inserted = 0, updated = 0;
        foreach (var p in posts)
        {
            var existing = await _db.BlogPosts.FirstOrDefaultAsync(x => x.Slug == p.Slug);
            if (existing == null)
            {
                _db.BlogPosts.Add(p);
                inserted++;
            }
            else
            {
                existing.Title       = p.Title;
                existing.Summary     = p.Summary;
                existing.Content     = p.Content;
                existing.Author      = p.Author;
                existing.IsPublished = p.IsPublished;
                if (existing.PublishedAt == default)
                    existing.PublishedAt = p.PublishedAt;
                updated++;
            }
            await EnsureSeoAsync(
                pageUrl:    $"/blog/{p.Slug}",
                title:      $"{p.Title} \u2014 AIInsights365.net",
                description: p.Summary,
                keywords:   "AIInsights365, AI analytics, AI insights, phase 1, phase 2, TOML, DAX, ETL, " + p.Slug.Replace('-', ' '),
                priority:   0.8m,
                changeFreq: "weekly");
        }
        await _db.SaveChangesAsync();
        _log.LogInformation("ContentSeeder: inserted {Inserted} / updated {Updated} BlogPost(s).", inserted, updated);
    }

    // ───────────────────── SEO helper ─────────────────────
    private async Task EnsureSeoAsync(string pageUrl, string title, string description,
        string keywords, decimal priority, string changeFreq)
    {
        if (await _db.SeoEntries.AnyAsync(s => s.PageUrl == pageUrl))
            return;

        _db.SeoEntries.Add(new SeoEntry
        {
            PageUrl          = pageUrl,
            Title            = title,
            MetaDescription  = description,
            MetaKeywords     = keywords,
            OgTitle          = title,
            OgDescription    = description,
            SitemapPriority  = priority,
            SitemapChangeFreq = changeFreq,
            IncludeInSitemap = true,
            CreatedBy        = "system",
            CreatedAt        = DateTime.UtcNow,
            LastModified     = DateTime.UtcNow
        });
    }
}
