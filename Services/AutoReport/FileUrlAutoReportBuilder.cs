using AIInsights.Models;
using System.Text;

namespace AIInsights.Services.AutoReport;

/// <summary>
/// Auto-report builder for File URL datasources (CSV / XLSX public share
/// links). The dataQuery is always "FILE_URL" — at execution time the
/// framework parses the file and the AI maps columns to chart axes.
/// </summary>
public sealed class FileUrlAutoReportBuilder : AutoReportBuilderBase
{
    private readonly IQueryExecutionService _queryService;

    public FileUrlAutoReportBuilder(IQueryExecutionService queryService)
    {
        _queryService = queryService;
    }

    public override bool CanHandle(string? dsType) =>
        !string.IsNullOrEmpty(dsType) && QueryExecutionService.FileUrlTypes.Contains(dsType);

    protected override (string queryRules, string kpiExample, string chartExample) GetQueryRules() =>
    (
        "- For \"dataQuery\", set it to \"FILE_URL\" — the system fetches and parses the file automatically.\n- Do NOT generate SQL for File URL datasources.",
        "\"dataQuery\": \"FILE_URL\"",
        "\"dataQuery\": \"FILE_URL\""
    );

    public override async Task<string> BuildSchemaSnippetAsync(Datasource ds)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## File Data Schema");
        try
        {
            var fileResult = await _queryService.ExecuteFileUrlAsync(ds, 5);
            if (fileResult.Success && fileResult.Data.Count > 0)
            {
                var fields = fileResult.Data.First().Keys.ToList();
                var tableName = ds.Name?.Replace(" ", "_") ?? "file_data";
                sb.AppendLine($"- **{tableName}** (File): {string.Join(", ", fields)}");
                sb.AppendLine($"- Row count: {fileResult.Data.Count}");
                sb.AppendLine("### Sample Data:");
                foreach (var row in fileResult.Data.Take(3))
                {
                    var vals = row.Select(kv => $"{kv.Key}={kv.Value}");
                    sb.AppendLine($"  - {string.Join(", ", vals)}");
                }
            }
            else
            {
                sb.AppendLine("- Could not retrieve sample data from the file.");
            }
        }
        catch
        {
            sb.AppendLine("- Could not retrieve file data for schema.");
        }
        return sb.ToString();
    }
}
