using AIInsights.Models;

namespace AIInsights.Services.AutoReport;

/// <summary>
/// Per-datasource auto-report builder. Each datasource type (SQL, Power BI,
/// REST API, File URL) ships its own implementation so the prompt rules and
/// schema-introspection logic stay isolated and easy to maintain.
/// </summary>
public interface IAutoReportBuilder
{
    /// <summary>Returns true when this builder is responsible for the given datasource type string.</summary>
    bool CanHandle(string? dsType);

    /// <summary>Builds the human-readable schema snippet that gets injected into the prompt.</summary>
    Task<string> BuildSchemaSnippetAsync(Datasource ds);

    /// <summary>Renders the full system prompt for this datasource type.</summary>
    string BuildSystemPrompt(string tables, string schemaSnippet, string? existingCharts, string relationshipsSnippet);
}

/// <summary>
/// Shared base — owns the giant prompt template that is identical across all
/// datasource types. Concrete builders only supply the per-type query rules
/// and the schema introspection logic.
/// </summary>
public abstract class AutoReportBuilderBase : IAutoReportBuilder
{
    public abstract bool CanHandle(string? dsType);
    public abstract Task<string> BuildSchemaSnippetAsync(Datasource ds);

    /// <summary>
    /// Returns the per-datasource fragments injected into the prompt:
    /// (queryRules, kpiExample, chartExample).
    /// </summary>
    protected abstract (string queryRules, string kpiExample, string chartExample) GetQueryRules();

    public string BuildSystemPrompt(string tables, string schemaSnippet, string? existingCharts, string relationshipsSnippet)
    {
        var (queryRules, kpiExample, chartExample) = GetQueryRules();

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
- Schema column type tags appear AFTER the closing bracket inside parentheses, e.g. `[Revenue] (decimal, numeric)` or `[OrderDate] (datetime, date)`. The tag is METADATA — it is NOT part of the column name.
  - Columns tagged `numeric` are safe to use in SUM/AVG/COUNT.
  - Columns tagged `date` are safe for time-series / trend charts.
  - WRITE THE COLUMN AS `[Revenue]` IN YOUR SQL — never as `[Revenue numeric]`, `[Revenue (decimal)]`, or anything that includes the type tag.
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
  - ""valueField"": the primary numeric column/field name. **For kpi/card: always set to ""Value"" (the mandatory alias).** For all other chart types: use the actual column name exactly as it appears in the schema (NOT a SQL alias — use the real schema column name for the aggregated field, tagged `numeric`).
  - ""description"": 1-2 sentence explanation of what this chart shows
  - ""tableName"": the table name this chart queries (must match one of the available tables)
  - ""width"": grid width per Layout Rules (title textbox: 12, KPI: 2, middle chart: 4, bottom table: 12)
  - ""height"": pixel height (shape-textbox: 90, kpi/card: 220, middle chart: 320, bottom table: 380)
{queryRules}

## Chart-Type Guidance — pick SIMPLE, SENSIBLE visuals the user can actually read
- PREFER these basic chart types: kpi/card, bar, line, pie/doughnut, table. Use scatter/area sparingly and only when the data clearly supports them.
- ""kpi"" and ""card"" = the single-metric card visual. Use them for ANY single-number metric (total, average, count, max, min). Use ""kpi"" when a delta-vs-prior indicator is meaningful; use ""card"" for a cleaner, plain single-value tile.
- CRITICAL KPI/CARD QUERY RULE — the query MUST return EXACTLY ONE ROW with ONE numeric column aliased [Value]. **Use a real table from `## Available Tables` below, NOT the placeholders shown here.** These examples are pattern-only:
  - PATTERN: SELECT COUNT(*) AS [Value] FROM [SchemaName].[TableName]
  - PATTERN: SELECT AVG([NumericColumn]) AS [Value] FROM [SchemaName].[TableName]
  - PATTERN: SELECT SUM([NumericColumn]) AS [Value] FROM [SchemaName].[TableName]
  - PATTERN: SELECT COUNT(*) AS [Value] FROM [SchemaName].[TableName] WHERE [FlagColumn] = 1
  - WRONG:   SELECT [Name], [Price] FROM [SchemaName].[TableName]   (multi-row, renders as bar chart)
  - WRONG:   SELECT [Category], COUNT(*) AS [Value] FROM [SchemaName].[TableName] GROUP BY [Category]   (multi-row)
  - NEVER use GROUP BY in a kpi/card query. NEVER select more than one column. NEVER use TOP N for kpi/card — it must aggregate to a scalar.
  - For kpi/card, set both ""labelField"" and ""valueField"" to ""Value"".
- ""bar"" / ""column"" — use for categorical comparisons (top N items, counts by category). Query: GROUP BY a category + aggregate a `numeric` column, ORDER BY the aggregate DESC, TOP 10. Set labelField to the GROUP BY column name, valueField to the numeric column name.
- ""line"" / ""area"" — use ONLY when you have a real `date` column and want a trend. Group by month/year and order chronologically. Set labelField to the date column, valueField to the numeric column.
- ""pie"" / ""doughnut"" — use ONLY for part-of-whole with a small category count (≤ 8 slices). Never use on high-cardinality columns (IDs, names, descriptions). Set labelField to the category column, valueField to the numeric column.
- ""table"" — use for detail rows (top-N lists). NEVER select long-text columns (Description, Notes, Comment, XML/JSON blobs); pick short ID/name/numeric columns only.
- DO NOT invent charts over unknown columns. If a column's purpose is unclear, skip it. Better to generate fewer, meaningful charts than many confusing ones.
- Every chart's query MUST make obvious sense: aggregating a clearly `numeric` field, grouping by a clearly categorical field.
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
          ""tableName"": ""<replace-with-a-table-from-Available-Tables>"",
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
          ""tableName"": ""<replace-with-a-table-from-Available-Tables>"",
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
          ""tableName"": ""<replace-with-a-table-from-Available-Tables>"",
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
          ""tableName"": ""<replace-with-a-table-from-Available-Tables>"",
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
          ""tableName"": ""<replace-with-a-table-from-Available-Tables>"",
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
          ""tableName"": ""<replace-with-a-table-from-Available-Tables>"",
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
          ""tableName"": ""<replace-with-a-table-from-Available-Tables>"",
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
          ""tableName"": ""<replace-with-a-table-from-Available-Tables>"",
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
          ""tableName"": ""<replace-with-a-table-from-Available-Tables>"",
          ""description"": ""Table showing top products by revenue."",
          ""width"": 12,
          ""height"": 380
        }}
      ]
    }}
  ]
}}";
    }
}
