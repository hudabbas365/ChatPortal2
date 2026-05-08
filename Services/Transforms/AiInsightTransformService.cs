using AIInsights.Models;
using Tomlyn;

namespace AIInsights.Services.Transforms;

public interface IAiInsightTransformService
{
    bool HasEnabledTransform(Datasource ds);
    QueryExecutionResult Apply(Datasource ds, QueryExecutionResult result);
    QueryExecutionResult ApplyWithSources(Datasource ds, QueryExecutionResult result, Dictionary<string, List<Dictionary<string, object>>>? sourceData);
    TransformParseResult Parse(string? toml);
}

public sealed class TransformParseResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public AiTransformDefinition? Definition { get; set; }
}

public sealed class AiTransformDefinition
{
    public bool Enabled { get; set; } = true;
    public bool DaxLikeExpressions { get; set; } = true;
    public string Name { get; set; } = "AI Insight Transform";
    public List<AiTransformRule> Rules { get; set; } = new();
}

public sealed class AiTransformRule
{
    public string Type { get; set; } = "";
    public Dictionary<string, object> Raw { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AiInsightTransformService : IAiInsightTransformService
{
    private readonly IReadOnlyDictionary<string, ITransformRuleHandler> _handlers;

    public AiInsightTransformService(IEnumerable<ITransformRuleHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasEnabledTransform(Datasource ds) =>
        ds != null
        && ds.TransformEnabled
        && !string.IsNullOrWhiteSpace(ds.TransformToml);

    public TransformParseResult Parse(string? toml)
    {
        if (string.IsNullOrWhiteSpace(toml))
            return new TransformParseResult { Success = true, Definition = new AiTransformDefinition { Enabled = false } };

        try
        {
            if (Toml.ToModel(toml) is not IDictionary<string, object> root)
                return new TransformParseResult { Success = false, Error = "Invalid transform TOML document." };

            var def = new AiTransformDefinition
            {
                Name = RuleHelpers.ReadString(root, "name", "AI Insight Transform"),
                Enabled = RuleHelpers.ReadBool(root, "enabled", true),
                DaxLikeExpressions = RuleHelpers.ReadBool(root, "dax_like_expressions", true),
            };

            if (root.TryGetValue("transform", out var transformObj) &&
                transformObj is IDictionary<string, object> transform)
            {
                def.Name = RuleHelpers.ReadString(transform, "name", def.Name);
                def.Enabled = RuleHelpers.ReadBool(transform, "enabled", def.Enabled);
                def.DaxLikeExpressions = RuleHelpers.ReadBool(transform, "dax_like_expressions", def.DaxLikeExpressions);
            }

            if (root.TryGetValue("rules", out var rulesObj) && rulesObj is IEnumerable<object> rules)
            {
                foreach (var rule in rules)
                {
                    if (rule is not IDictionary<string, object> r) continue;
                    var type = RuleHelpers.ReadString(r, "type", "").Trim();
                    if (string.IsNullOrEmpty(type)) continue;
                    def.Rules.Add(new AiTransformRule
                    {
                        Type = type,
                        Raw = new Dictionary<string, object>(r, StringComparer.OrdinalIgnoreCase)
                    });
                }
            }

            return new TransformParseResult { Success = true, Definition = def };
        }
        catch (Exception ex)
        {
            return new TransformParseResult { Success = false, Error = $"Transform TOML parse failed: {ex.Message}" };
        }
    }

    public QueryExecutionResult Apply(Datasource ds, QueryExecutionResult result)
        => ApplyWithSources(ds, result, null);

    public QueryExecutionResult ApplyWithSources(
        Datasource ds,
        QueryExecutionResult result,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData)
    {
        if (!result.Success || !HasEnabledTransform(ds)) return result;

        var parsed = Parse(ds.TransformToml);
        if (!parsed.Success || parsed.Definition == null)
            return new QueryExecutionResult
            {
                Success = false,
                Error = parsed.Error ?? "Transform parse failed."
            };

        if (!parsed.Definition.Enabled || parsed.Definition.Rules.Count == 0) return result;

        var rows = result.Data.Select(r => new Dictionary<string, object>(r, StringComparer.OrdinalIgnoreCase)).ToList();
        var audit = new List<string> { $"Transform '{parsed.Definition.Name}' applied to {rows.Count} rows." };

        try
        {
            foreach (var rule in parsed.Definition.Rules)
            {
                var type = rule.Type.Trim();
                if (!_handlers.TryGetValue(type, out var handler))
                {
                    // Unknown rule type: skip silently (validation should catch this before execution)
                    continue;
                }

                // Pass dax_like_expressions flag via the rule's raw dict so DerivedFieldHandler can read it
                if (!rule.Raw.ContainsKey("dax_like_expressions"))
                    rule.Raw["dax_like_expressions"] = parsed.Definition.DaxLikeExpressions;

                try
                {
                    rows = handler.Apply(rule.Raw, rows, sourceData, audit);
                }
                catch (TransformAbortException abortEx)
                {
                    audit.Add(abortEx.AuditError);
                    return new QueryExecutionResult
                    {
                        Success = false,
                        Error = abortEx.Message,
                        TransformAudit = audit
                    };
                }
            }

            return new QueryExecutionResult
            {
                Success = true,
                Data = rows,
                RowCount = rows.Count,
                TransformAudit = audit
            };
        }
        catch (Exception ex)
        {
            audit.Add("Transform failure: " + ex.Message);
            return new QueryExecutionResult
            {
                Success = false,
                Error = $"Transform execution failed: {ex.Message}",
                TransformAudit = audit
            };
        }
    }

}
