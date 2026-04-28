using AIInsights.Models;
using System.Text;

namespace AIInsights.Services.AutoReport;

/// <summary>
/// Auto-report builder for REST API datasources. The dataQuery is always
/// "REST_API" — at execution time the framework hits the configured endpoint
/// and the AI's job is purely to map JSON fields to chart axes.
/// </summary>
public sealed class RestApiAutoReportBuilder : AutoReportBuilderBase
{
    private readonly IQueryExecutionService _queryService;

    public RestApiAutoReportBuilder(IQueryExecutionService queryService)
    {
        _queryService = queryService;
    }

    public override bool CanHandle(string? dsType) =>
        !string.IsNullOrEmpty(dsType) && QueryExecutionService.RestApiTypes.Contains(dsType);

    protected override (string queryRules, string kpiExample, string chartExample) GetQueryRules() =>
    (
        "- For \"dataQuery\", set it to \"REST_API\" — the system fetches data automatically.\n- Do NOT generate SQL for REST API datasources.",
        "\"dataQuery\": \"REST_API\"",
        "\"dataQuery\": \"REST_API\""
    );

    public override async Task<string> BuildSchemaSnippetAsync(Datasource ds)
    {
        var sb = new StringBuilder();
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
        return sb.ToString();
    }
}
