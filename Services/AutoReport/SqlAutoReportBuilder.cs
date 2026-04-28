using AIInsights.Models;
using System.Text;

namespace AIInsights.Services.AutoReport;

/// <summary>
/// Auto-report builder for SQL Server datasources. Introspects sys.objects so
/// BOTH tables (U) and views (V) are surfaced, ranks by row count, and tags
/// numeric/date columns so the AI picks aggregatable fields automatically.
/// </summary>
public sealed class SqlAutoReportBuilder : AutoReportBuilderBase
{
    private readonly IQueryExecutionService _queryService;

    public SqlAutoReportBuilder(IQueryExecutionService queryService)
    {
        _queryService = queryService;
    }

    public override bool CanHandle(string? dsType)
    {
        if (string.IsNullOrEmpty(dsType)) return true; // default fallback
        return !QueryExecutionService.PowerBiTypes.Contains(dsType)
            && !QueryExecutionService.RestApiTypes.Contains(dsType)
            && !QueryExecutionService.FileUrlTypes.Contains(dsType);
    }

    protected override (string queryRules, string kpiExample, string chartExample) GetQueryRules() =>
    (
        "- For \"dataQuery\", generate valid SQL Server T-SQL SELECT queries.\n" +
        "- TABLE SOURCE — the ONLY tables/views you may use are the ones printed in the `### Schemas → Tables` index in the schema snippet below. That index IS the complete allow-list. Anything not in it does not exist in this database.\n" +
        "  - Read the index, pick a table, and write its schema-qualified bracket form EXACTLY as printed (e.g. `[SalesLT].[Customer]`).\n" +
        "  - Never invent tables. Never strip/replace the schema prefix. Never pluralise/singularise.\n" +
        "  - CORRECT:   FROM [SalesLT].[Customer]\n" +
        "  - WRONG:     FROM [dbo].[Customer]                  -- schema replaced\n" +
        "  - WRONG:     FROM [SalesLT].[Customers]             -- pluralised\n" +
        "  - WRONG:     FROM Customer                          -- missing schema + brackets\n" +
        "- KNOWN HALLUCINATION PATTERNS — DO NOT EMIT. These are common BI-codebase names that LLMs invent but DO NOT exist here:\n" +
        "  - `vw_CustomerSummary`, `vw_TopProducts`, `vw_MonthlyRevenue`, `vw_SalesByRegion`, `vw_OrderSummary`, or any `vw_*` name unless that exact string appears in the index.\n" +
        "  - Generic plurals: `Sales`, `Orders`, `Customers`, `Products` — use the singular schema-prefixed form from the index instead.\n" +
        "  - Before writing FROM/JOIN, scan the index for the literal string. If you cannot find it character-for-character, pick a different metric — do NOT fabricate.\n" +
        "- COLUMN NAME RULE — every column reference MUST be wrapped in square brackets so names with spaces, reserved words, or special characters work:\n" +
        "  - `[Order Date]`, `[Customer Name]`, `[Total $]`, `[Order]` (reserved keyword) — all valid because of the brackets.\n" +
        "  - The bracket holds ONLY the bare column name. NEVER include the type/kind tag inside: `[Revenue numeric]`, `[Revenue (decimal)]`, `[Revenue★]`, `[Revenue⏱]` are ALL WRONG. The `(sqlType, kind)` annotation in the schema is metadata, not part of the name.\n" +
        "  - CORRECT:   SUM([Order Total])\n" +
        "  - CORRECT:   GROUP BY [Customer Name]\n" +
        "  - WRONG:     SUM([Order Total numeric])   -- metadata leaked inside\n" +
        "  - WRONG:     SUM(Order Total)             -- unbracketed name with space → SQL error\n" +
        "- Aliases for SELECT/AS: bracket-wrap the alias too whenever it contains spaces (e.g. `AS [Total Revenue]`).\n" +
        "- ALIAS vs COLUMN — CRITICAL: the alias after `AS` is the OUTPUT label you choose; it is NEVER itself a source column. Do NOT pass an alias name back into `SUM`/`AVG`/`MIN`/`MAX`/`COUNT` unless that exact name also appears in the `### Column Details` for the FROM table.\n" +
        "  - WRONG (hallucinated alias-as-column):\n" +
        "      SELECT [AddressType], SUM([AddressCount]) AS [AddressCount] FROM [SalesLT].[CustomerAddress] GROUP BY [AddressType]\n" +
        "      -- `[AddressCount]` is NOT a column on `CustomerAddress`; it is the desired output label.\n" +
        "  - CORRECT — use `COUNT(*)` when the user wants \"count of rows per category\":\n" +
        "      SELECT [AddressType], COUNT(*) AS [AddressCount] FROM [SalesLT].[CustomerAddress] GROUP BY [AddressType]\n" +
        "  - CORRECT — use `SUM([RealNumericColumn])` only when the column is in the schema list:\n" +
        "      SELECT [AddressType], SUM([Quantity]) AS [TotalQuantity] FROM [SalesLT].[Order] GROUP BY [AddressType]\n" +
        "- COUNT-BY-CATEGORY HEURISTIC: if the user prompt asks for a \"count\", \"number of\", \"distribution\", or \"how many X by Y\", and there is NO numeric column whose semantics match the count, ALWAYS use `COUNT(*)` (or `COUNT([PrimaryKey])`) — never invent a `[*Count]` / `[Total*]` / `[Num*]` column.\n" +
        "- Limit row-returning queries to TOP 100. KPI/card queries must aggregate to a single scalar (no TOP, no GROUP BY).\n" +
        "- Only reference columns explicitly listed for that table/view in the `### Column Details` section — never assume `[Id]`, `[Name]`, `[CreatedDate]`, `[*Count]`, `[Total*]`, etc. exist.\n" +
        "- Before emitting each `dataQuery`, re-scan it: every bracketed identifier inside `SUM(...)`, `AVG(...)`, `MIN(...)`, `MAX(...)`, `COUNT(...)` (except `*`), and every bracketed name in `SELECT`/`WHERE`/`GROUP BY`/`ORDER BY` must appear character-for-character in the `### Column Details` row for the FROM table. If it doesn't, rewrite the query before emitting.",
        "\"dataQuery\": \"SELECT COUNT(*) AS [Value] FROM [SchemaName].[TableName]\"",
        "\"dataQuery\": \"SELECT TOP 10 [CategoryColumn], COUNT(*) AS [RowCount] FROM [SchemaName].[TableName] GROUP BY [CategoryColumn] ORDER BY [RowCount] DESC\""
    );

    public override async Task<string> BuildSchemaSnippetAsync(Datasource ds)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## SQL Server Schema");
        sb.AppendLine("Format: `[ColumnName] (sqlType, kind)` where kind is `numeric` (safe to aggregate), `date` (safe for time-series), or omitted for text/other. The kind tag is metadata — NEVER include it inside brackets in your SQL.");
        try
        {
            const string schemaQuery = @"
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
            if (!result.Success || result.Data.Count == 0) return sb.ToString();

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

            // ── Dynamic Schemas → Tables index ─────────────────────────
            // Built from the same result set (no extra query). Lets the AI
            // see at a glance which schemas exist and which tables live in
            // each, so it never confuses `[dbo].[Customer]` with the real
            // `[SalesLT].[Customer]`.
            var schemaIndex = result.Data
                .Select(r => new
                {
                    Schema = r.ContainsKey("SchemaName") ? r["SchemaName"]?.ToString() ?? "dbo" : "dbo",
                    Table = r.ContainsKey("TableName") ? r["TableName"]?.ToString() ?? "" : "",
                    Kind = r.ContainsKey("ObjectKind") ? r["ObjectKind"]?.ToString() ?? "TABLE" : "TABLE"
                })
                .Where(x => !string.IsNullOrEmpty(x.Table))
                .GroupBy(x => x.Schema)
                .OrderBy(g => g.Key);

            sb.AppendLine();
            sb.AppendLine("### Schemas → Tables (use these schema prefixes verbatim)");
            foreach (var schemaGroup in schemaIndex)
            {
                var distinctObjects = schemaGroup
                    .GroupBy(x => x.Table)
                    .Select(g => new { Name = g.Key, Kind = g.First().Kind })
                    .OrderBy(x => x.Name)
                    .ToList();
                var rendered = distinctObjects.Select(o => o.Kind == "VIEW" ? $"[{o.Name}] (view)" : $"[{o.Name}]");
                sb.AppendLine($"- **[{schemaGroup.Key}]**: {string.Join(", ", rendered)}");
            }
            sb.AppendLine();
            sb.AppendLine("### Column Details (top 50 objects by row count)");

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
                .OrderByDescending(g =>
                {
                    var tblName = g.Key.Split('.').LastOrDefault()?.Trim('[', ']', '|') ?? "";
                    tblName = tblName.Split('|')[0].Trim('[', ']');
                    return rowCounts.TryGetValue(tblName, out var rc) ? rc : 0;
                })
                .Take(50);

            foreach (var tbl in grouped)
            {
                var parts = tbl.Key.Split('|');
                var qualified = parts[0];
                var kind = parts.Length > 1 ? parts[1] : "TABLE";

                var tblNameOnly = qualified.Split('.').LastOrDefault()?.Trim('[', ']') ?? "";
                var rowCountStr = rowCounts.TryGetValue(tblNameOnly, out var rc) ? $", ~{FormatRowCount(rc)} rows" : "";

                var colRows = tbl.ToList();
                var cols = colRows.Take(30).Select(r =>
                {
                    var name = r.ContainsKey("ColumnName") ? r["ColumnName"]?.ToString() ?? "" : "";
                    var dtype = r.ContainsKey("DataType") ? r["DataType"]?.ToString() ?? "" : "";
                    var kindTag = numericTypes.Contains(dtype) ? ", numeric" : (dateTypes.Contains(dtype) ? ", date" : "");
                    return $"[{name}] ({dtype}{kindTag})";
                });
                var truncationNote = colRows.Count > 30 ? $", ...and {colRows.Count - 30} more columns" : "";
                sb.AppendLine($"- **{qualified}** ({kind}{rowCountStr}): {string.Join(", ", cols)}{truncationNote}");
            }
        }
        catch
        {
            sb.AppendLine("- Could not retrieve SQL Server schema.");
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
