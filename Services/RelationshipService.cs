using System.Collections.Concurrent;
using System.Text;
using AIInsights.Models;

namespace AIInsights.Services;

public class RelationshipInfo
{
    public string FromTable { get; set; } = "";
    public string FromColumn { get; set; } = "";
    public string ToTable { get; set; } = "";
    public string ToColumn { get; set; } = "";
    public string Source { get; set; } = ""; // "fk" | "dax" | "ai" | "heuristic"
    public double Confidence { get; set; } = 1.0;
}

public interface IRelationshipService
{
    Task<List<RelationshipInfo>> GetRelationshipsAsync(Datasource ds);
    void InvalidateCache(int datasourceId);
}

/// <summary>
/// Discovers table relationships across SQL Server, Power BI, and REST API datasources.
/// SQL Server uses FK introspection. Power BI uses INFO.RELATIONSHIPS() DAX. REST API
/// and any non-relational source fall back to an AI-inferred graph via Cohere.
/// Results are cached in-memory per datasource for 15 minutes.
/// </summary>
public class RelationshipService : IRelationshipService
{
    private readonly IQueryExecutionService _queryService;
    private readonly CohereService _cohere;
    private readonly ILogger<RelationshipService> _logger;
    private static readonly ConcurrentDictionary<int, (DateTime At, List<RelationshipInfo> Rels)> _cache = new();
    private static readonly TimeSpan _ttl = TimeSpan.FromMinutes(15);

    public RelationshipService(IQueryExecutionService queryService, CohereService cohere, ILogger<RelationshipService> logger)
    {
        _queryService = queryService;
        _cohere = cohere;
        _logger = logger;
    }

    public void InvalidateCache(int datasourceId) => _cache.TryRemove(datasourceId, out _);

    public async Task<List<RelationshipInfo>> GetRelationshipsAsync(Datasource ds)
    {
        if (_cache.TryGetValue(ds.Id, out var hit) && DateTime.UtcNow - hit.At < _ttl)
            return hit.Rels;

        var result = new List<RelationshipInfo>();
        var type = ds.Type ?? "";
        try
        {
            if (QueryExecutionService.PowerBiTypes.Contains(type))
                result = await FromPowerBiAsync(ds);
            else if (type.Contains("SQL Server", StringComparison.OrdinalIgnoreCase)
                     || type.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                     || type.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
                result = await FromSqlServerAsync(ds);
            else if (QueryExecutionService.RestApiTypes.Contains(type))
                result = await FromAiAsync(ds);

            // If native introspection returned nothing, try AI as a last resort
            if (result.Count == 0)
                result = await FromAiAsync(ds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Relationship discovery failed for datasource {Id}", ds.Id);
        }

        _cache[ds.Id] = (DateTime.UtcNow, result);
        return result;
    }

    private async Task<List<RelationshipInfo>> FromSqlServerAsync(Datasource ds)
    {
        // Read-only query allowed by QueryExecutionService safety gate (starts with SELECT).
        const string sql = @"SELECT
    OBJECT_SCHEMA_NAME(fk.parent_object_id) + '.' + OBJECT_NAME(fk.parent_object_id) AS fromTable,
    cp.name AS fromColumn,
    OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + OBJECT_NAME(fk.referenced_object_id) AS toTable,
    cr.name AS toColumn
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.columns cp ON fkc.parent_column_id = cp.column_id AND fkc.parent_object_id = cp.object_id
JOIN sys.columns cr ON fkc.referenced_column_id = cr.column_id AND fkc.referenced_object_id = cr.object_id";

        var r = await _queryService.ExecuteReadOnlyAsync(ds, sql, 5000);
        if (!r.Success) return new();
        return r.Data.Select(row => new RelationshipInfo
        {
            FromTable = GetStr(row, "fromTable"),
            FromColumn = GetStr(row, "fromColumn"),
            ToTable = GetStr(row, "toTable"),
            ToColumn = GetStr(row, "toColumn"),
            Source = "fk",
            Confidence = 1.0
        }).Where(x => !string.IsNullOrEmpty(x.FromColumn) && !string.IsNullOrEmpty(x.ToColumn)).ToList();
    }

    private async Task<List<RelationshipInfo>> FromPowerBiAsync(Datasource ds)
    {
        try
        {
            const string relSql = "EVALUATE SELECTCOLUMNS(INFO.RELATIONSHIPS(), \"FromTableID\", [FromTableID], \"FromColumnID\", [FromColumnID], \"ToTableID\", [ToTableID], \"ToColumnID\", [ToColumnID], \"IsActive\", [IsActive])";
            const string tblSql = "EVALUATE SELECTCOLUMNS(INFO.TABLES(), \"ID\", [ID], \"Name\", [Name])";
            const string colSql = "EVALUATE SELECTCOLUMNS(INFO.COLUMNS(), \"ID\", [ID], \"TableID\", [TableID], \"ExplicitName\", [ExplicitName])";

            var relR = await _queryService.ExecuteReadOnlyAsync(ds, relSql, 5000);
            var tblR = await _queryService.ExecuteReadOnlyAsync(ds, tblSql, 5000);
            var colR = await _queryService.ExecuteReadOnlyAsync(ds, colSql, 20000);
            if (!relR.Success || !tblR.Success || !colR.Success) return new();

            var tMap = tblR.Data.ToDictionary(r => GetStr(r, "ID"), r => GetStr(r, "Name"));
            var cMap = colR.Data.ToDictionary(r => GetStr(r, "ID"), r => new { TableId = GetStr(r, "TableID"), Name = GetStr(r, "ExplicitName") });

            var result = new List<RelationshipInfo>();
            foreach (var rel in relR.Data)
            {
                if (!cMap.TryGetValue(GetStr(rel, "FromColumnID"), out var fc)) continue;
                if (!cMap.TryGetValue(GetStr(rel, "ToColumnID"), out var tc)) continue;
                if (!tMap.TryGetValue(GetStr(rel, "FromTableID"), out var fromTable)) continue;
                if (!tMap.TryGetValue(GetStr(rel, "ToTableID"), out var toTable)) continue;
                result.Add(new RelationshipInfo
                {
                    FromTable = fromTable,
                    FromColumn = fc.Name,
                    ToTable = toTable,
                    ToColumn = tc.Name,
                    Source = "dax",
                    Confidence = 1.0
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Power BI relationship discovery failed for datasource {Id}", ds.Id);
            return new();
        }
    }

    /// <summary>
    /// AI-inferred relationships. Sends the schema (tables + columns) to Cohere and
    /// asks it to enumerate likely FK-style joins. Best-effort — returns empty on
    /// any failure (missing API key, malformed response, timeout).
    /// </summary>
    private async Task<List<RelationshipInfo>> FromAiAsync(Datasource ds)
    {
        try
        {
            // Build a compact schema description from the datasource's selected tables,
            // falling back to running the generic schema query if available.
            var schemaDump = await BuildSchemaDumpAsync(ds);
            if (string.IsNullOrWhiteSpace(schemaDump)) return new();

            var prompt = "Given the following tables and columns, infer likely foreign-key style relationships between them. " +
                         "Return ONLY a JSON array — no prose, no markdown — of objects with fields: " +
                         "fromTable, fromColumn, toTable, toColumn, confidence (0..1). " +
                         "Only include relationships you are reasonably confident about (confidence >= 0.6). " +
                         "SCHEMA:\n" + schemaDump;

            var sb = new StringBuilder();
            await foreach (var chunk in _cohere.StreamChatAsync(prompt, new List<(string, string)>(), "You are a database schema analyzer. Respond with raw JSON only."))
                sb.Append(chunk);

            var raw = sb.ToString();
            // Strip markdown code fences if the model added any
            var start = raw.IndexOf('[');
            var end = raw.LastIndexOf(']');
            if (start < 0 || end <= start) return new();
            var json = raw.Substring(start, end - start + 1);

            var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
            if (parsed == null) return new();

            return parsed.Select(d => new RelationshipInfo
            {
                FromTable = GetDictStr(d, "fromTable"),
                FromColumn = GetDictStr(d, "fromColumn"),
                ToTable = GetDictStr(d, "toTable"),
                ToColumn = GetDictStr(d, "toColumn"),
                Source = "ai",
                Confidence = double.TryParse(GetDictStr(d, "confidence"), out var c) ? c : 0.7
            }).Where(x => !string.IsNullOrEmpty(x.FromTable)
                          && !string.IsNullOrEmpty(x.FromColumn)
                          && !string.IsNullOrEmpty(x.ToTable)
                          && !string.IsNullOrEmpty(x.ToColumn)
                          && x.Confidence >= 0.6).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AI relationship inference failed for datasource {Id}", ds.Id);
            return new();
        }
    }

    private async Task<string> BuildSchemaDumpAsync(Datasource ds)
    {
        // Prefer the selected-tables list when known to keep the prompt small.
        var selected = string.IsNullOrWhiteSpace(ds.SelectedTables)
            ? Array.Empty<string>()
            : ds.SelectedTables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var sb = new StringBuilder();
        if (QueryExecutionService.RestApiTypes.Contains(ds.Type ?? ""))
        {
            try
            {
                var api = await _queryService.ExecuteRestApiAsync(ds, 5);
                if (api.Success && api.Data.Count > 0)
                {
                    sb.Append("Table ").Append(ds.Name).Append(": ");
                    sb.Append(string.Join(", ", api.Data[0].Keys));
                    sb.Append('\n');
                }
            }
            catch { /* ignore */ }
            return sb.ToString();
        }

        // For relational sources, use the generic schema query when available.
        var schemaSql = ds.Type?.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) == true
            ? "SELECT t.TABLE_SCHEMA + '.' + t.TABLE_NAME as table_name, c.COLUMN_NAME as column_name, c.DATA_TYPE as data_type FROM INFORMATION_SCHEMA.TABLES t JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION"
            : null;
        if (schemaSql == null) return "";

        var r = await _queryService.ExecuteReadOnlyAsync(ds, schemaSql, 5000);
        if (!r.Success) return "";
        foreach (var grp in r.Data.GroupBy(row => GetStr(row, "table_name")))
        {
            if (selected.Length > 0 && !selected.Any(s => grp.Key.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
            sb.Append("Table ").Append(grp.Key).Append(": ");
            sb.Append(string.Join(", ", grp.Select(c => $"{GetStr(c, "column_name")}({GetStr(c, "data_type")})")));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string GetStr(Dictionary<string, object> row, string key)
    {
        if (row.TryGetValue(key, out var v) && v != null) return v.ToString() ?? "";
        // Power BI sometimes returns bracket-wrapped keys
        if (row.TryGetValue("[" + key + "]", out var v2) && v2 != null) return v2.ToString() ?? "";
        return "";
    }

    private static string GetDictStr(Dictionary<string, object> d, string key)
        => d.TryGetValue(key, out var v) && v != null ? v.ToString() ?? "" : "";
}
