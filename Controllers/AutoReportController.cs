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

    public AutoReportController(CohereService cohere, IQueryExecutionService queryService, AppDbContext db)
    {
        _cohere = cohere;
        _queryService = queryService;
        _db = db;
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

            var systemPrompt = BuildSystemPrompt(ds?.Type, tables, schemaSnippet, req.ExistingCharts);

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

    private string BuildSystemPrompt(string? dsType, string tables, string schemaSnippet, string? existingCharts)
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

## Rules
- Return ONLY valid JSON — no markdown, no explanation, no code fences.
- The JSON must be an object with a ""pages"" array.
- Each page has: ""name"" (string), ""charts"" (array).
- Each chart has:
  - ""chartType"": one of ""bar"", ""line"", ""pie"", ""doughnut"", ""area"", ""scatter"", ""table"", ""kpi"", ""card"" (single-metric card visual, interchangeable with kpi), ""shape-textbox""
  - ""title"": short descriptive title (max ~40 chars)
  - ""dataQuery"": see query rules below
  - ""labelField"": the column/field for labels/categories (MUST be an actual column name from the schema, NOT a SQL alias)
  - ""valueField"": the primary numeric column/field (MUST be an actual column name from the schema, NOT a SQL alias)
  - ""description"": 1-2 sentence explanation of what this chart shows
  - ""tableName"": the table name this chart queries (must match one of the available tables)
  - ""width"": grid width 3-12 (default 6)
  - ""height"": pixel height (shape-textbox: 80, kpi: 200, table: 380, charts: 300-320)
{queryRules}

## Chart-Type Guidance — pick SIMPLE, SENSIBLE visuals the user can actually read
- PREFER these basic chart types: kpi/card, bar, line, pie/doughnut, table. Use scatter/area sparingly and only when the data clearly supports them.
- ""kpi"" and ""card"" = the single-metric card visual. Use them for ANY single-number metric (total, average, count, max, min). Use ""kpi"" when a delta-vs-prior indicator is meaningful; use ""card"" for a cleaner, plain single-value tile.
- CRITICAL KPI/CARD QUERY RULE — the query MUST return EXACTLY ONE ROW with ONE numeric column aliased [Value]:
  - CORRECT:  SELECT COUNT(*) AS [Value] FROM [dbo].[Products]
  - CORRECT:  SELECT AVG([ListPrice]) AS [Value] FROM [dbo].[Products]
  - CORRECT:  SELECT SUM([Revenue]) AS [Value] FROM [dbo].[Sales]
  - CORRECT:  SELECT COUNT(*) AS [Value] FROM [dbo].[Products] WHERE [Discontinued] = 1
  - WRONG:    SELECT [Name], [Price] FROM [dbo].[Products]   (multi-row → renders as bar chart)
  - WRONG:    SELECT [Category], COUNT(*) AS [Value] FROM ... GROUP BY [Category]   (multi-row)
  - NEVER use GROUP BY in a kpi/card query. NEVER select more than one column. NEVER use TOP N for kpi/card — it must aggregate to a scalar.
  - For kpi/card, set both ""labelField"" and ""valueField"" to ""Value"".
- ""bar"" / ""column"" — use for categorical comparisons (top N items, counts by category). Query: GROUP BY a category + aggregate, ORDER BY the aggregate DESC, LIMIT/TOP 10.
- ""line"" / ""area"" — use ONLY when you have a real date/time column and want a trend. Group by month/year and order chronologically.
- ""pie"" / ""doughnut"" — use ONLY for part-of-whole with a small category count (≤ 8 slices). Never use on high-cardinality columns (IDs, names, descriptions).
- ""table"" — use for detail rows (top-N lists). NEVER select long-text columns (Description, Notes, Comment, XML/JSON blobs); pick short ID/name/numeric columns only.
- DO NOT invent charts over unknown columns. If a column's purpose is unclear, skip it. Better to generate fewer, meaningful charts than many confusing ones.
- Every chart's query MUST make obvious sense: aggregating a clearly numeric field, grouping by a clearly categorical field.
- Use ""shape-textbox"" charts for page titles and report descriptions. Set ""text"" field with the content.
- For KPI cards, use chartType ""kpi"" with a query that returns a single aggregated value aliased [Value].

## Layout Rules
- Each page MUST have 6-8 charts (including KPI cards and text boxes).
- Spread charts across 2-4 pages. Name pages descriptively (e.g. ""Overview"", ""Sales Analysis"", ""Trends"").
- On page 1, include a shape-textbox (width 12) with a report title and brief description.
- Start each page with 3-4 KPI cards (width 3, height 200) in a row — these are the ""cards"" at the top of the dashboard.
- Follow the KPI row with 1-2 medium charts side by side (width 6 each), then a full-width chart or table (width 12) below.
- Use a variety of chart types across the report (kpi, bar, line, pie, table).
- Plan chart widths so they tile in rows of 12 columns total (e.g. four width-3 cards, two width-6 charts, one width-12 full-width). Avoid leftover gaps.
{redesignNote}
## Available Tables
{tables}

{schemaSnippet}

## Example Output (notice 8 charts per page — always aim for 7-8)
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
          ""height"": 80
        }},
        {{
          ""chartType"": ""kpi"",
          ""title"": ""Total Revenue"",
          {kpiExample},
          ""labelField"": ""Value"",
          ""valueField"": ""Value"",
          ""tableName"": ""Sales"",
          ""description"": ""Shows total revenue."",
          ""width"": 3,
          ""height"": 200
        }},
        {{
          ""chartType"": ""kpi"",
          ""title"": ""Total Orders"",
          {kpiExample},
          ""labelField"": ""Value"",
          ""valueField"": ""Value"",
          ""tableName"": ""Sales"",
          ""description"": ""Total number of orders."",
          ""width"": 3,
          ""height"": 200
        }},
        {{
          ""chartType"": ""kpi"",
          ""title"": ""Avg Order Value"",
          {kpiExample},
          ""labelField"": ""Value"",
          ""valueField"": ""Value"",
          ""tableName"": ""Sales"",
          ""description"": ""Average order value."",
          ""width"": 3,
          ""height"": 200
        }},
        {{
          ""chartType"": ""kpi"",
          ""title"": ""Total Customers"",
          {kpiExample},
          ""labelField"": ""Value"",
          ""valueField"": ""Value"",
          ""tableName"": ""Sales"",
          ""description"": ""Unique customer count."",
          ""width"": 3,
          ""height"": 200
        }},
        {{
          ""chartType"": ""bar"",
          ""title"": ""Revenue by Region"",
          {chartExample},
          ""labelField"": ""Region"",
          ""valueField"": ""TotalRevenue"",
          ""tableName"": ""Sales"",
          ""description"": ""Bar chart showing revenue distribution across regions."",
          ""width"": 6,
          ""height"": 300
        }},
        {{
          ""chartType"": ""pie"",
          ""title"": ""Sales by Category"",
          {chartExample},
          ""labelField"": ""Category"",
          ""valueField"": ""TotalSales"",
          ""tableName"": ""Sales"",
          ""description"": ""Pie chart of sales distribution by category."",
          ""width"": 6,
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
        // ── SQL Server: use sys.tables / sys.columns ──
        else
        {
            sb.AppendLine("## SQL Server Schema");
            try
            {
                var schemaQuery = @"
                    SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName, ty.name AS DataType
                    FROM sys.tables t
                    JOIN sys.schemas s ON t.schema_id = s.schema_id
                    JOIN sys.columns c ON c.object_id = t.object_id
                    JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                    ORDER BY s.name, t.name, c.column_id";
                var result = await _queryService.ExecuteReadOnlyAsync(ds, schemaQuery);
                if (result.Success && result.Data.Count > 0)
                {
                    var grouped = result.Data
                        .GroupBy(r =>
                        {
                            var schema = r.ContainsKey("SchemaName") ? r["SchemaName"]?.ToString() ?? "dbo" : "dbo";
                            var table = r.ContainsKey("TableName") ? r["TableName"]?.ToString() ?? "" : "";
                            return $"[{schema}].[{table}]";
                        })
                        .Where(g => !string.IsNullOrEmpty(g.Key));
                    foreach (var tbl in grouped)
                    {
                        var cols = tbl.Select(r =>
                        {
                            var name = r.ContainsKey("ColumnName") ? r["ColumnName"]?.ToString() ?? "" : "";
                            var dtype = r.ContainsKey("DataType") ? r["DataType"]?.ToString() ?? "" : "";
                            return $"[{name}] ({dtype})";
                        });
                        sb.AppendLine($"- **{tbl.Key}**: {string.Join(", ", cols)}");
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
}
