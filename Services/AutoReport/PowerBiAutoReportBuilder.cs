using AIInsights.Models;
using System.Text;

namespace AIInsights.Services.AutoReport;

/// <summary>
/// Auto-report builder for Power BI / Analysis Services tabular models.
/// Schema is introspected via the DMV $SYSTEM.MDSCHEMA_COLUMNS, and queries
/// are emitted as DAX (EVALUATE / SUMMARIZECOLUMNS).
/// </summary>
public sealed class PowerBiAutoReportBuilder : AutoReportBuilderBase
{
    private readonly IQueryExecutionService _queryService;

    public PowerBiAutoReportBuilder(IQueryExecutionService queryService)
    {
        _queryService = queryService;
    }

    public override bool CanHandle(string? dsType) =>
        !string.IsNullOrEmpty(dsType) && QueryExecutionService.PowerBiTypes.Contains(dsType);

    protected override (string queryRules, string kpiExample, string chartExample) GetQueryRules() =>
    (
        "- For \"dataQuery\", generate valid DAX queries.\n- Use EVALUATE and SUMMARIZE for aggregations.\n- Reference tables and columns exactly as shown in the schema.",
        "\"dataQuery\": \"EVALUATE ROW(\\\"Value\\\", CALCULATE(SUM('Sales'[Revenue])))\"",
        "\"dataQuery\": \"EVALUATE SUMMARIZECOLUMNS('Sales'[Region], \\\"TotalRevenue\\\", SUM('Sales'[Revenue]))\""
    );

    public override async Task<string> BuildSchemaSnippetAsync(Datasource ds)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Power BI Data Model");
        try
        {
            const string dmvQuery = "SELECT [TABLE_NAME], [COLUMN_NAME], [DATA_TYPE] FROM $SYSTEM.MDSCHEMA_COLUMNS WHERE [TABLE_NAME] NOT LIKE '$%'";
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
        return sb.ToString();
    }
}
