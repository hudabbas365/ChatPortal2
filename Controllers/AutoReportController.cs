using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AIInsights.Controllers;

[Authorize]
[Route("api/auto-report")]
[ApiController]
public class AutoReportController : ControllerBase
{
    private readonly CohereService _cohere;
    private readonly IQueryExecutionService _queryService;
    private readonly AppDbContext _db;
    private readonly IRelationshipService _relationships;

    public AutoReportController(CohereService cohere, IQueryExecutionService queryService, AppDbContext db, IRelationshipService relationships)
    {
        _cohere = cohere;
        _queryService = queryService;
        _db = db;
        _relationships = relationships;
    }

    public class AutoReportRequest
    {
        public string? WorkspaceId { get; set; }
        public string? UserId { get; set; }
        public string? DatasourceId { get; set; }
        public string? Prompt { get; set; }
        public List<string> TableNames { get; set; } = new();
        public string? ExistingCharts { get; set; }
    }

    [HttpPost("generate")]
    public async Task Generate([FromBody] AutoReportRequest req)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            // Enforce plan feature gate: only FreeTrial + Enterprise can run the
            // AI auto-report generator. Professional is chat+dashboard only.
            // The check is keyed on the user's ASSIGNED license (SubscriptionPlan)
            // rather than org.Plan so that per-user license assignments are
            // honoured in mixed-license organisations.
            if (!string.IsNullOrEmpty(req.UserId))
            {
                var userSub = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == req.UserId);
                PlanType effectivePlan;
                if (userSub != null)
                {
                    effectivePlan = userSub.Plan;
                }
                else
                {
                    var user = await _db.Users.FindAsync(req.UserId);
                    var org = user?.OrganizationId != null ? await _db.Organizations.FindAsync(user.OrganizationId) : null;
                    effectivePlan = org?.Plan ?? PlanType.Free;
                }
                if (!PlanFeatures.AllowsAutoReport(effectivePlan))
                {
                    await Response.WriteAsync("data: {\"error\":\"AI Auto-Report generation is not available on your current plan. Upgrade to Enterprise to unlock this feature.\",\"code\":\"plan_gated\"}\n\n");
                    await Response.WriteAsync("data: [DONE]\n\n");
                    await Response.Body.FlushAsync();
                    return;
                }
            }

            // Resolve datasource
            Datasource? ds = null;
            if (!string.IsNullOrEmpty(req.DatasourceId))
            {
                if (int.TryParse(req.DatasourceId, out var dsId))
                    ds = await _db.Datasources.FindAsync(dsId);
            }
            if (ds == null && !string.IsNullOrEmpty(req.WorkspaceId))
            {
                var wsGuid = req.WorkspaceId;
                {
                    var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == wsGuid);
                    if (ws != null)
                        ds = await _db.Datasources.FirstOrDefaultAsync(d => d.WorkspaceId == ws.Id);
                }
            }

            // Build schema snippet
            var schemaSnippet = "";
            if (ds != null)
            {
                try { schemaSnippet = await BuildSchemaSnippetAsync(ds); }
                catch { schemaSnippet = "Schema not available."; }
            }

            var tables = req.TableNames.Count > 0
                ? string.Join(", ", req.TableNames)
                : "No specific tables provided";

            // Discover table relationships so the AI emits FK-correct JOINs.
            var relationshipsSnippet = "";
            if (ds != null)
            {
                try
                {
                    var rels = await _relationships.GetRelationshipsAsync(ds);
                    relationshipsSnippet = BuildRelationshipsSnippet(rels);
                }
                catch { /* best-effort */ }
            }

            var systemPrompt = BuildSystemPrompt(ds?.Type, tables, schemaSnippet, req.ExistingCharts, relationshipsSnippet);

            var userPrompt = string.IsNullOrWhiteSpace(req.Prompt)
                ? "Generate a comprehensive multi-page report covering all available data with KPIs, charts, and tables."
                : req.Prompt;

            if (!string.IsNullOrWhiteSpace(req.ExistingCharts))
            {
                userPrompt += "\n\n## Existing Charts to Redesign:\n" + req.ExistingCharts;
            }

            // Stream AI response
            var history = new List<(string role, string content)>();
            await foreach (var chunk in _cohere.StreamChatAsync(userPrompt, history, systemPrompt))
            {
                await Response.WriteAsync($"data: {{\"text\":\"{EscapeJson(chunk)}\"}}\n\n");
                await Response.Body.FlushAsync();
            }

            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            await Response.WriteAsync($"data: {{\"error\":\"{EscapeJson(ex.Message)}\"}}\n\n");
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
        }
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private static string BuildRelationshipsSnippet(IReadOnlyList<RelationshipInfo>? rels)
    {
        if (rels == null || rels.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Known Table Relationships (use these for JOINs)");
        sb.AppendLine("The left-hand column is a foreign key referencing the right-hand column. Prefer these when writing multi-table queries — do NOT invent joins that are not listed here.");
        foreach (var r in rels.Take(40))
        {
            sb.AppendLine($"- {r.FromTable}.{r.FromColumn} -> {r.ToTable}.{r.ToColumn}" + (string.IsNullOrEmpty(r.Source) ? "" : $"  (source: {r.Source})"));
        }
        return sb.ToString();
    }

    private string BuildSystemPrompt(string? dsType, string tables, string schemaSnippet, string? existingCharts, string relationshipsSnippet = "")
    {
        var isPbi = QueryExecutionService.PowerBiTypes.Contains(dsType ?? "");
        var isRest = QueryExecutionService.RestApiTypes.Contains(dsType ?? "");

        string queryRules, kpiExample, chartExample;

        if (isRest)
        {
            queryRules = "- For \"dataQuery\", set it to \"REST_API\" — the system fetches data automatically.\n- Do NOT generate SQL for REST API datasources.";
            kpiExample = "\"dataQuery\": \"REST_API\"";
            chartExample = "\"dataQuery\": \"REST_API\"";
        }
        else if (isPbi)
        {
            queryRules = "- For \"dataQuery\", generate valid DAX queries.\n- Use EVALUATE and SUMMARIZE for aggregations.\n- Reference tables and columns exactly as shown in the schema.";
            kpiExample = "\"dataQuery\": \"EVALUATE ROW(\\\"Value\\\", CALCULATE(SUM('Sales'[Revenue])))\"";
            chartExample = "\"dataQuery\": \"EVALUATE SUMMARIZECOLUMNS('Sales'[Region], \\\"TotalRevenue\\\", SUM('Sales'[Revenue]))\"";
        }
        else
        {
            queryRules = "- For \"dataQuery\", generate valid SQL SELECT queries.\n- ALWAYS wrap SQL column names in square brackets.\n- Limit queries to TOP 100 rows.\n- Use fully schema-qualified table names from the schema below.";
            kpiExample = "\"dataQuery\": \"SELECT SUM([Revenue]) AS [Value] FROM [dbo].[Sales]\"";
            chartExample = "\"dataQuery\": \"SELECT TOP 10 [Region], SUM([Revenue]) AS [TotalRevenue] FROM [dbo].[Sales] GROUP BY [Region] ORDER BY [TotalRevenue] DESC\"";
        }

        var redesignNote = "";
        if (!string.IsNullOrWhiteSpace(existingCharts))
        {
            redesignNote = "\n- REDESIGN MODE: The user has existing charts (listed in the user message). Analyze them and create an improved version with better layout, more insights, and varied chart types.\n";
        }

        return $@"You are an expert BI report designer. Given a data schema, generate a structured JSON report plan.

## CRITICAL COLUMN RULE — READ FIRST
- ONLY reference columns that are EXPLICITLY listed in the schema snippet below for the exact table/view you are querying.
- NEVER invent columns. NEVER assume columns like [ModifiedDate], [CreatedDate], [Id], [Name] exist unless they appear in the snippet for that specific object.
- NEVER write ORDER BY on a column that is not in that table/view's listed columns. If no suitable sort column exists, OMIT ORDER BY entirely.
- Items marked (VIEW) are views — their column list in the schema is AUTHORITATIVE; treat any column not listed as non-existent.
- If you need a metric and the chosen object has no matching column, pick a DIFFERENT object from the schema or skip that chart. Do NOT guess.
- Schema column markers: ★ = numeric/aggregatable (safe to use in SUM/AVG/COUNT), ⏱ = date/time (safe for time-series and trend charts).
- Tables show an approximate row count (e.g. ""~50k rows""). PREFER tables with more rows — they contain real data. Avoid querying tables with 0 or very few rows.

## Rules
- Return ONLY valid JSON — no markdown, no explanation, no code fences.
- The JSON must be an object with a ""pages"" array.
- Each page has: ""name"" (string), ""charts"" (array).
- Each chart has:
  - ""chartType"": one of ""bar"", ""line"", ""pie"", ""doughnut"", ""area"", ""scatter"", ""table"", ""kpi"", ""card"" (single-metric card visual, interchangeable with kpi), ""shape-textbox""
  - ""title"": short descriptive title (max ~40 chars)
  - ""dataQuery"": see query rules below
  - ""labelField"": the column/field name for labels/categories. **For kpi/card: always set to ""Value"" (the mandatory alias).** For all other chart types: use the actual column name exactly as it appears in the schema (NOT a SQL alias — use the real schema column name for the GROUP BY field).
  - ""valueField"": the primary numeric column/field name. **For kpi/card: always set to ""Value"" (the mandatory alias).** For all other chart types: use the actual column name exactly as it appears in the schema (NOT a SQL alias — use the real schema column name for the aggregated field, marked ★).
  - ""description"": 1-2 sentence explanation of what this chart shows
  - ""tableName"": the table name this chart queries (must match one of the available tables)
  - ""width"": grid width per Layout Rules (title textbox: 12, KPI: 2, middle chart: 4, bottom table: 12)
  - ""height"": pixel height (shape-textbox: 90, kpi/card: 220, middle chart: 320, bottom table: 380)
{queryRules}

## Chart-Type Guidance — pick SIMPLE, SENSIBLE visuals the user can actually read
- PREFER these basic chart types: kpi/card, bar, line, pie/doughnut, table. Use scatter/area sparingly and only when the data clearly supports them.
- ""kpi"" and ""card"" = the single-metric card visual. Use them for ANY single-number metric (total, average, count, max, min). Use ""kpi"" when a delta-vs-prior indicator is meaningful; use ""card"" for a cleaner, plain single-value tile.
- CRITICAL KPI/CARD QUERY RULE — the query MUST return EXACTLY ONE ROW with ONE numeric column aliased [Value]:
  - CORRECT:  SELECT COUNT(*) AS [Value] FROM [dbo].[Products]
  - CORRECT:  SELECT AVG([ListPrice★]) AS [Value] FROM [dbo].[Products]
  - CORRECT:  SELECT SUM([Revenue★]) AS [Value] FROM [dbo].[Sales]
  - CORRECT:  SELECT COUNT(*) AS [Value] FROM [dbo].[Products] WHERE [Discontinued] = 1
  - WRONG:    SELECT [Name], [Price] FROM [dbo].[Products]   (multi-row → renders as bar chart)
  - WRONG:    SELECT [Category], COUNT(*) AS [Value] FROM ... GROUP BY [Category]   (multi-row)
  - NEVER use GROUP BY in a kpi/card query. NEVER select more than one column. NEVER use TOP N for kpi/card — it must aggregate to a scalar.
  - For kpi/card, set both ""labelField"" and ""valueField"" to ""Value"".
- ""bar"" / ""column"" — use for categorical comparisons (top N items, counts by category). Query: GROUP BY a category + aggregate a ★ column, ORDER BY the aggregate DESC, TOP 10. Set labelField to the GROUP BY column name, valueField to the ★ column name.
- ""line"" / ""area"" — use ONLY when you have a real ⏱ date/time column and want a trend. Group by month/year and order chronologically. Set labelField to the date column, valueField to the ★ column.
- ""pie"" / ""doughnut"" — use ONLY for part-of-whole with a small category count (≤ 8 slices). Never use on high-cardinality columns (IDs, names, descriptions). Set labelField to the category column, valueField to the ★ column.
- ""table"" — use for detail rows (top-N lists). NEVER select long-text columns (Description, Notes, Comment, XML/JSON blobs); pick short ID/name/numeric columns only.
- DO NOT invent charts over unknown columns. If a column's purpose is unclear, skip it. Better to generate fewer, meaningful charts than many confusing ones.
- Every chart's query MUST make obvious sense: aggregating a clearly ★ numeric field, grouping by a clearly categorical field.
- Use ""shape-textbox"" charts for page titles and report descriptions. Set ""text"" field with the content.
- For KPI cards, use chartType ""kpi"" with a query that returns a single aggregated value aliased [Value].

## Layout Rules
- Each page MUST follow this exact structure, top-to-bottom:
  1. A **title textbox** at the very top: chartType ""shape-textbox"", width 12, height 90, with a bold title + 1-line description in the ""text"" field.
  2. A **row of exactly 5 KPI cards** (chartType ""kpi"" or ""card""). Use width 2 and height 220 for each so they span columns 1..10 — leave the last 2 columns empty on that row (do NOT squeeze a 6th card in).
  3. A **row of exactly 3 middle visuals** (bar/line/pie/doughnut/area). Use width 4 and height 320 for each so they tile perfectly across 12 columns.
  4. A **full-width table** at the bottom: chartType ""table"", width 12, height 380.
- This 4-tier skeleton is MANDATORY. Do not replace it with 4 KPIs or 2 charts. If the page would have fewer than 5 meaningful KPIs, generate extra aggregates (counts, totals, averages, distincts) from the schema to fill the row.
- Spread the report across 2-4 pages. Name pages descriptively (e.g. ""Overview"", ""Sales Analysis"", ""Trends""). EVERY page uses the same 4-tier skeleton above.
- Use a variety of chart types across the report for the middle 3-visual row (mix bar, line, pie, doughnut, area).
- KPIs at the top — keep their titles short (max ~18 chars) so they fit the width-2 card.
{redesignNote}
## Available Tables
{tables}

{schemaSnippet}
{relationshipsSnippet}
## Example Output (MANDATORY skeleton per page: 1 title textbox → 5 KPIs → 3 charts → 1 table)
{{
  ""pages"": [
    {{
      ""name"": ""Overview"",
      ""charts"": [
        {{
          ""chartType"": ""shape-textbox"",
          ""title"": ""Report Title"",
          ""text"": ""Analytics Report\nGenerated overview of key metrics and trends."",
          ""tableName"": """",
          ""width"": 12,
          ""height"": 90
        }},
        {{
          ""chartType"": ""kpi"",
          ""title"": ""Total Revenue"",
          {kpiExample},
          ""labelField"": ""Value"",
          ""valueField"": ""Value"",
          ""tableName"": ""Sales"",
          ""description"": ""Shows total revenue."",
          ""width"": 2,
          ""height"": 220
        }},
        {{
          ""chartType"": ""kpi"",
          ""title"": ""Total Orders"",
          {kpiExample},
          ""labelField"": ""Value"",
          ""valueField"": ""Value"",
          ""tableName"": ""Sales"",
          ""description"": ""Total number of orders."",
          ""width"": 2,
          ""height"": 220
        }},
        {{
          ""chartType"": ""kpi"",
          ""title"": ""Avg Order Value"",
          {kpiExample},
          ""labelField"": ""Value"",
          ""valueField"": ""Value"",
          ""tableName"": ""Sales"",
          ""description"": ""Average order value."",
          ""width"": 2,
          ""height"": 220
        }},
        {{
          ""chartType"": ""kpi"",
          ""title"": ""Total Customers"",
          {kpiExample},
          ""labelField"": ""Value"",
          ""valueField"": ""Value"",
          ""tableName"": ""Sales"",
          ""description"": ""Unique customer count."",
          ""width"": 2,
          ""height"": 220
        }},
        {{
          ""chartType"": ""card"",
          ""title"": ""Active Regions"",
          {kpiExample},
          ""labelField"": ""Value"",
          ""valueField"": ""Value"",
          ""tableName"": ""Sales"",
          ""description"": ""Number of regions with sales."",
          ""width"": 2,
          ""height"": 220
        }},
        {{
          ""chartType"": ""bar"",
          ""title"": ""Revenue by Region"",
          {chartExample},
          ""labelField"": ""Region"",
          ""valueField"": ""TotalRevenue"",
          ""tableName"": ""Sales"",
          ""description"": ""Bar chart showing revenue distribution across regions."",
          ""width"": 4,
          ""height"": 320
        }},
        {{
          ""chartType"": ""line"",
          ""title"": ""Revenue Trend"",
          {chartExample},
          ""labelField"": ""Month"",
          ""valueField"": ""TotalRevenue"",
          ""tableName"": ""Sales"",
          ""description"": ""Monthly revenue trend over time."",
          ""width"": 4,
          ""height"": 320
        }},
        {{
          ""chartType"": ""pie"",
          ""title"": ""Sales by Category"",
          {chartExample},
          ""labelField"": ""Category"",
          ""valueField"": ""TotalSales"",
          ""tableName"": ""Sales"",
          ""description"": ""Pie chart of sales distribution by category."",
          ""width"": 4,
          ""height"": 320
        }},
        {{
          ""chartType"": ""table"",
          ""title"": ""Top 10 Products"",
          {chartExample},
          ""labelField"": ""Product"",
          ""valueField"": ""Revenue"",
          ""tableName"": ""Sales"",
          ""description"": ""Table showing top products by revenue."",
          ""width"": 12,
          ""height"": 380
        }}
      ]
    }}
  ]
}}";
    }

    private async Task<string> BuildSchemaSnippetAsync(Datasource ds)
    {
        var sb = new StringBuilder();
        var isPbi = QueryExecutionService.PowerBiTypes.Contains(ds.Type ?? "");
        var isRest = QueryExecutionService.RestApiTypes.Contains(ds.Type ?? "");

        // ── REST API: get field names from sample data ──
        if (isRest)
        {
            sb.AppendLine("## API Data Schema");
            try
            {
                var apiResult = await _queryService.ExecuteRestApiAsync(ds);
                if (apiResult.Success && apiResult.Data.Count > 0)
                {
                    var fields = apiResult.Data.First().Keys.ToList();
                    var tableName = ds.Name?.Replace(" ", "_") ?? "api_data";
                    sb.AppendLine($"- **{tableName}** (API Endpoint): {string.Join(", ", fields)}");
                    sb.AppendLine($"- Row count: {apiResult.Data.Count}");
                    sb.AppendLine("### Sample Data:");
                    foreach (var row in apiResult.Data.Take(3))
                    {
                        var vals = row.Select(kv => $"{kv.Key}={kv.Value}");
                        sb.AppendLine($"  - {string.Join(", ", vals)}");
                    }
                }
                else
                {
                    sb.AppendLine("- Could not retrieve sample data from the API.");
                }
            }
            catch
            {
                sb.AppendLine("- Could not retrieve API data for schema.");
            }
        }
        // ── Power BI: use DMV to get table/column info ──
        else if (isPbi)
        {
            sb.AppendLine("## Power BI Data Model");
            try
            {
                var dmvQuery = "SELECT [TABLE_NAME], [COLUMN_NAME], [DATA_TYPE] FROM $SYSTEM.MDSCHEMA_COLUMNS WHERE [TABLE_NAME] NOT LIKE '$%'";
                var result = await _queryService.ExecuteReadOnlyAsync(ds, dmvQuery);
                if (result.Success && result.Data.Count > 0)
                {
                    var grouped = result.Data
                        .GroupBy(r => r.ContainsKey("TABLE_NAME") ? r["TABLE_NAME"]?.ToString() ?? "" : "")
                        .Where(g => !string.IsNullOrEmpty(g.Key));
                    foreach (var tbl in grouped)
                    {
                        var cols = tbl.Select(r =>
                        {
                            var name = r.ContainsKey("COLUMN_NAME") ? r["COLUMN_NAME"]?.ToString() ?? "" : "";
                            var dtype = r.ContainsKey("DATA_TYPE") ? r["DATA_TYPE"]?.ToString() ?? "" : "";
                            return string.IsNullOrEmpty(dtype) ? name : $"{name} ({dtype})";
                        });
                        sb.AppendLine($"- **{tbl.Key}**: {string.Join(", ", cols)}");
                    }
                }
            }
            catch
            {
                sb.AppendLine("- Could not retrieve Power BI model schema.");
            }
        }
        // ── SQL Server: use sys.objects so BOTH tables (U) AND views (V) are included ──
        else
        {
            sb.AppendLine("## SQL Server Schema");
            sb.AppendLine("Column markers: ★ = numeric/aggregatable, ⏱ = date/time");
            try
            {
                var schemaQuery = @"
                    SELECT s.name AS SchemaName,
                           o.name AS TableName,
                           c.name AS ColumnName,
                           ty.name AS DataType,
                           CASE o.type WHEN 'V' THEN 'VIEW' ELSE 'TABLE' END AS ObjectKind
                    FROM sys.objects o
                    JOIN sys.schemas s ON o.schema_id = s.schema_id
                    JOIN sys.columns c ON c.object_id = o.object_id
                    JOIN sys.types  ty ON c.user_type_id = ty.user_type_id
                    WHERE o.type IN ('U','V')
                    ORDER BY s.name, o.name, c.column_id";
                var result = await _queryService.ExecuteReadOnlyAsync(ds, schemaQuery);
                if (result.Success && result.Data.Count > 0)
                {
                    // Fetch estimated row counts from partition stats (best-effort).
                    var rowCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        const string rowCountQuery = @"
                            SELECT o.name AS TableName, SUM(p.row_count) AS RowCount
                            FROM sys.objects o
                            JOIN sys.dm_db_partition_stats p ON p.object_id = o.object_id
                            WHERE o.type IN ('U','V') AND p.index_id IN (0, 1)
                            GROUP BY o.name";
                        var rcResult = await _queryService.ExecuteReadOnlyAsync(ds, rowCountQuery);
                        if (rcResult.Success)
                        {
                            foreach (var row in rcResult.Data)
                            {
                                var tblName = row.ContainsKey("TableName") ? row["TableName"]?.ToString() ?? "" : "";
                                var cnt = row.ContainsKey("RowCount") && long.TryParse(row["RowCount"]?.ToString(), out var n) ? n : 0;
                                if (!string.IsNullOrEmpty(tblName)) rowCounts[tblName] = cnt;
                            }
                        }
                    }
                    catch { /* best-effort: row counts are informational only */ }

                    // Column type classifiers.
                    var numericTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "float", "real", "money", "smallmoney", "bit" };
                    var dateTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { "datetime", "datetime2", "date", "time", "datetimeoffset", "smalldatetime" };

                    var grouped = result.Data
                        .GroupBy(r =>
                        {
                            var schema = r.ContainsKey("SchemaName") ? r["SchemaName"]?.ToString() ?? "dbo" : "dbo";
                            var table = r.ContainsKey("TableName") ? r["TableName"]?.ToString() ?? "" : "";
                            var kind = r.ContainsKey("ObjectKind") ? r["ObjectKind"]?.ToString() ?? "TABLE" : "TABLE";
                            return $"[{schema}].[{table}]|{kind}";
                        })
                        .Where(g => !string.IsNullOrEmpty(g.Key))
                        // Sort most-populated tables first so the AI focuses on data-rich objects.
                        .OrderByDescending(g =>
                        {
                            var tblName = g.Key.Split('.').LastOrDefault()?.Trim('[', ']', '|') ?? "";
                            // Strip trailing |TABLE or |VIEW suffix that may be present.
                            tblName = tblName.Split('|')[0].Trim('[', ']');
                            return rowCounts.TryGetValue(tblName, out var rc) ? rc : 0;
                        })
                        .Take(50); // cap at 50 tables to prevent token overflow

                    foreach (var tbl in grouped)
                    {
                        var parts = tbl.Key.Split('|');
                        var qualified = parts[0];
                        var kind = parts.Length > 1 ? parts[1] : "TABLE";

                        // Extract unqualified table name for row-count lookup.
                        var tblNameOnly = qualified.Split('.').LastOrDefault()?.Trim('[', ']') ?? "";
                        var rowCountStr = rowCounts.TryGetValue(tblNameOnly, out var rc) ? $", ~{FormatRowCount(rc)} rows" : "";

                        var colRows = tbl.ToList();
                        var cols = colRows.Take(30).Select(r =>
                        {
                            var name = r.ContainsKey("ColumnName") ? r["ColumnName"]?.ToString() ?? "" : "";
                            var dtype = r.ContainsKey("DataType") ? r["DataType"]?.ToString() ?? "" : "";
                            var marker = numericTypes.Contains(dtype) ? "★" : (dateTypes.Contains(dtype) ? "⏱" : "");
                            return $"[{name}]{marker} ({dtype})";
                        });
                        var truncationNote = colRows.Count > 30 ? $", ...and {colRows.Count - 30} more columns" : "";
                        sb.AppendLine($"- **{qualified}** ({kind}{rowCountStr}): {string.Join(", ", cols)}{truncationNote}");
                    }
                }
            }
            catch
            {
                sb.AppendLine("- Could not retrieve SQL Server schema.");
            }
        }

        return sb.ToString();
    }

    private static string FormatRowCount(long n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000}M",
        >= 1_000 => $"{n / 1_000}k",
        _ => n.ToString()
    };
}
