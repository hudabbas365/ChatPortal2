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
                Name = ReadString(root, "name", "AI Insight Transform"),
                Enabled = ReadBool(root, "enabled", true),
                DaxLikeExpressions = ReadBool(root, "dax_like_expressions", true),
            };

            if (root.TryGetValue("transform", out var transformObj) &&
                transformObj is IDictionary<string, object> transform)
            {
                def.Name = ReadString(transform, "name", def.Name);
                def.Enabled = ReadBool(transform, "enabled", def.Enabled);
                def.DaxLikeExpressions = ReadBool(transform, "dax_like_expressions", def.DaxLikeExpressions);
            }

            if (root.TryGetValue("rules", out var rulesObj) && rulesObj is IEnumerable<object> rules)
            {
                foreach (var rule in rules)
                {
                    if (rule is not IDictionary<string, object> r) continue;
                    var type = ReadString(r, "type", "").Trim();
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
                var type = rule.Type.Trim().ToLowerInvariant();
                switch (type)
                {
                    case "remove_duplicates":
                        rows = RemoveDuplicates(rows, ReadStringList(rule.Raw, "keys"));
                        audit.Add("Data cleansing: removed duplicates.");
                        break;
                    case "handle_nulls":
                        ApplyNullHandling(rows, rule.Raw);
                        audit.Add("Data cleansing: handled null/missing values.");
                        break;
                    case "standardize_format":
                        ApplyStandardization(rows, rule.Raw);
                        audit.Add("Data cleansing: standardized value formats.");
                        break;
                    case "convert_units":
                        ApplyUnitConversion(rows, rule.Raw);
                        audit.Add("Normalization: unit conversion applied.");
                        break;
                    case "apply_naming_convention":
                        ApplyNamingConvention(rows, rule.Raw);
                        audit.Add("Normalization: naming convention applied.");
                        break;
                    case "map_codes":
                        ApplyCodeMapping(rows, rule.Raw);
                        audit.Add("Normalization: code mapping applied.");
                        break;
                    case "kpi_classification":
                        ApplyKpiClassification(rows, rule.Raw);
                        audit.Add("Business logic: KPI classification applied.");
                        break;
                    case "sentiment_score":
                        ApplySentimentScore(rows, rule.Raw);
                        audit.Add("Business logic: sentiment score computed.");
                        break;
                    case "derived_field":
                        ApplyDerivedField(rows, rule.Raw, parsed.Definition.DaxLikeExpressions);
                        audit.Add("Business logic: derived field computed.");
                        break;
                    case "aggregate":
                        rows = ApplyAggregation(rows, rule.Raw);
                        audit.Add("Aggregation: grouped and summarized.");
                        break;
                    case "append_rows":
                        rows = ApplyAppendRows(rows, rule.Raw, sourceData);
                        audit.Add("Combine: rows appended from selected source tables.");
                        break;
                    case "merge_tables":
                        var leftKey = ReadString(rule.Raw, "left_key", "");
                        var rightKey = ReadString(rule.Raw, "right_key", leftKey);
                        if (string.IsNullOrWhiteSpace(leftKey) || string.IsNullOrWhiteSpace(rightKey))
                        {
                            audit.Add("Combine warning: merge_tables skipped because left_key/right_key were not provided.");
                            break;
                        }
                        rows = ApplyMergeTables(rows, rule.Raw, sourceData);
                        audit.Add("Combine: table merge completed.");
                        break;
                    case "pivot_table":
                        rows = ApplyPivotTable(rows, rule.Raw);
                        audit.Add("Shape: pivot table applied.");
                        break;
                    case "unpivot_table":
                        rows = ApplyUnpivotTable(rows, rule.Raw);
                        audit.Add("Shape: unpivot table applied.");
                        break;
                    case "transpose_table":
                        rows = ApplyTransposeTable(rows, rule.Raw);
                        audit.Add("Shape: transpose table applied.");
                        break;
                    case "filter_rows":
                        rows = ApplyFilterRows(rows, rule.Raw);
                        audit.Add("Shape: filter rows applied.");
                        break;
                    case "sort_rows":
                        rows = ApplySortRows(rows, rule.Raw);
                        audit.Add("Shape: sort rows applied.");
                        break;
                    case "select_columns":
                        rows = ApplySelectColumns(rows, rule.Raw);
                        audit.Add("Shape: select columns applied.");
                        break;
                    case "rename_columns":
                        ApplyRenameColumns(rows, rule.Raw);
                        audit.Add("Shape: rename columns applied.");
                        break;
                    case "split_column":
                        ApplySplitColumn(rows, rule.Raw);
                        audit.Add("Clean: split column applied.");
                        break;
                    case "replace_values":
                        ApplyReplaceValues(rows, rule.Raw);
                        audit.Add("Clean: replace values applied.");
                        break;
                    case "cast_types":
                        ApplyCastTypes(rows, rule.Raw);
                        audit.Add("Validate: cast types applied.");
                        break;
                    case "flatten_json":
                        ApplyFlatten(rows, rule.Raw);
                        audit.Add("Restructuring: flattened nested JSON.");
                        break;
                    case "validate_schema":
                        var violations = ValidateSchema(rows, rule.Raw);
                        if (violations.Count > 0)
                        {
                            var strict = ReadBool(rule.Raw, "strict", true);
                            audit.AddRange(violations.Select(v => "Validation: " + v));
                            if (strict)
                            {
                                return new QueryExecutionResult
                                {
                                    Success = false,
                                    Error = "Transform validation failed: " + string.Join(" | ", violations.Take(5)),
                                    TransformAudit = audit
                                };
                            }
                        }
                        else
                        {
                            audit.Add("Validation: schema checks passed.");
                        }
                        break;
                    case "referential_integrity":
                        var riViolations = ValidateReferentialIntegrity(rows, rule.Raw);
                        if (riViolations.Count > 0)
                        {
                            audit.AddRange(riViolations.Select(v => "Referential integrity: " + v));
                            if (ReadBool(rule.Raw, "strict", true))
                            {
                                return new QueryExecutionResult
                                {
                                    Success = false,
                                    Error = "Transform referential integrity checks failed.",
                                    TransformAudit = audit
                                };
                            }
                        }
                        else
                        {
                            audit.Add("Validation: referential integrity checks passed.");
                        }
                        break;
                    case "log_transformations":
                        audit.Add("Audit: explicit log_transformations marker reached.");
                        break;
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

    private static List<Dictionary<string, object>> RemoveDuplicates(List<Dictionary<string, object>> rows, List<string> keys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outRows = new List<Dictionary<string, object>>();
        foreach (var row in rows)
        {
            var sig = keys.Count > 0
                ? string.Join("|", keys.Select(k => (row.TryGetValue(k, out var v) ? v : null)?.ToString() ?? ""))
                : string.Join("|", row.OrderBy(k => k.Key).Select(kv => $"{kv.Key}={kv.Value}"));
            if (seen.Add(sig)) outRows.Add(row);
        }
        return outRows;
    }

    private static void ApplyNullHandling(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var fields = ReadStringList(raw, "fields");
        var oneField = ReadString(raw, "field", "");
        if (!string.IsNullOrWhiteSpace(oneField)) fields.Add(oneField);
        if (fields.Count == 0) fields = rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var strategy = ReadString(raw, "strategy", "replace").ToLowerInvariant();
        var replacement = ReadString(raw, "value", "");
        foreach (var row in rows.ToArray())
        {
            var drop = false;
            foreach (var f in fields)
            {
                row.TryGetValue(f, out var v);
                var isMissing = v == null || string.IsNullOrWhiteSpace(v.ToString()) || string.Equals(v.ToString(), "NULL", StringComparison.OrdinalIgnoreCase);
                if (!isMissing) continue;
                if (strategy == "drop_row") { drop = true; break; }
                row[f] = replacement;
            }
            if (drop) rows.Remove(row);
        }
    }

    private static void ApplyStandardization(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var field = ReadString(raw, "field", "");
        if (string.IsNullOrWhiteSpace(field)) return;
        var format = ReadString(raw, "format", "text").ToLowerInvariant();
        var output = ReadString(raw, "output", "yyyy-MM-dd");
        foreach (var row in rows)
        {
            if (!row.TryGetValue(field, out var v) || v == null) continue;
            var s = v.ToString() ?? "";
            if (format == "date" && DateTime.TryParse(s, out var dt))
                row[field] = dt.ToString(output);
            else if (format == "currency" && decimal.TryParse(s, out var money))
                row[field] = money.ToString("0.00");
            else if (format == "upper")
                row[field] = s.ToUpperInvariant();
            else if (format == "lower")
                row[field] = s.ToLowerInvariant();
            else if (format == "title")
                row[field] = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        }
    }

    private static void ApplyUnitConversion(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var field = ReadString(raw, "field", "");
        if (string.IsNullOrWhiteSpace(field)) return;
        var target = ReadString(raw, "target_field", field);
        var from = ReadString(raw, "from", "").ToUpperInvariant();
        var to = ReadString(raw, "to", "").ToUpperInvariant();
        foreach (var row in rows)
        {
            if (!row.TryGetValue(field, out var v) || !double.TryParse(v?.ToString(), out var n)) continue;
            if (from == "MB" && to == "GB") n /= 1024d;
            else if (from == "GB" && to == "MB") n *= 1024d;
            else if (from == "SECONDS" && to == "MINUTES") n /= 60d;
            else if (from == "MINUTES" && to == "HOURS") n /= 60d;
            row[target] = Math.Round(n, 4);
        }
    }

    private static void ApplyNamingConvention(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var style = ReadString(raw, "style", "snake_case").ToLowerInvariant();
        var fields = ReadStringList(raw, "fields");
        if (fields.Count == 0) fields = rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var row in rows)
        {
            var updates = new Dictionary<string, object>();
            foreach (var f in fields)
            {
                if (!row.TryGetValue(f, out var v)) continue;
                var renamed = style switch
                {
                    "lower" => f.ToLowerInvariant(),
                    "upper" => f.ToUpperInvariant(),
                    "pascalcase" => ToPascalCase(f),
                    "camelcase" => ToCamelCase(f),
                    _ => ToSnakeCase(f),
                };
                updates[renamed] = v;
                if (!renamed.Equals(f, StringComparison.OrdinalIgnoreCase)) row.Remove(f);
            }
            foreach (var kv in updates) row[kv.Key] = kv.Value;
        }
    }

    private static void ApplyCodeMapping(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var field = ReadString(raw, "field", "");
        if (string.IsNullOrWhiteSpace(field)) return;
        var target = ReadString(raw, "target_field", field);
        var mapping = ReadStringDictionary(raw, "mapping");
        if (mapping.Count == 0) return;
        foreach (var row in rows)
        {
            if (!row.TryGetValue(field, out var v)) continue;
            var key = (v?.ToString() ?? "").Trim();
            if (mapping.TryGetValue(key, out var mapped)) row[target] = mapped;
        }
    }

    private static void ApplyKpiClassification(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var field = ReadString(raw, "field", "");
        if (string.IsNullOrWhiteSpace(field)) return;
        var target = ReadString(raw, "target_field", "KpiClass");
        var fr = new HashSet<string>(ReadStringList(raw, "fr_values"), StringComparer.OrdinalIgnoreCase);
        var notFr = new HashSet<string>(ReadStringList(raw, "not_fr_values"), StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var val = row.TryGetValue(field, out var v) ? (v?.ToString() ?? "") : "";
            if (fr.Contains(val)) row[target] = "FR";
            else if (notFr.Contains(val)) row[target] = "Not FR";
            else row[target] = "N/A";
        }
    }

    private static void ApplySentimentScore(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var field = ReadString(raw, "field", "");
        if (string.IsNullOrWhiteSpace(field)) return;
        var target = ReadString(raw, "target_field", "SentimentScore");
        foreach (var row in rows)
        {
            var text = row.TryGetValue(field, out var v) ? (v?.ToString() ?? "") : "";
            row[target] = ScoreSentiment(text);
        }
    }

    private static void ApplyDerivedField(List<Dictionary<string, object>> rows, Dictionary<string, object> raw, bool daxLike)
    {
        var target = ReadString(raw, "target_field", "");
        var expr = ReadString(raw, "expression", "");
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(expr)) return;
        foreach (var row in rows)
            row[target] = EvaluateExpression(expr, row, daxLike) ?? "NULL";
    }

    private static List<Dictionary<string, object>> ApplyAggregation(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var groups = ReadStringList(raw, "group_by");
        var metricSpecs = ReadStringList(raw, "metrics");
        if (groups.Count == 0 || metricSpecs.Count == 0) return rows;
        var grouped = rows.GroupBy(r => string.Join("\u001f", groups.Select(g =>
        {
            var val = r.TryGetValue(g, out var v) ? v?.ToString() ?? "" : "";
            return val.Replace("\u001f", "\\u001f", StringComparison.Ordinal);
        })));
        var output = new List<Dictionary<string, object>>();
        foreach (var g in grouped)
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var first = g.FirstOrDefault() ?? new Dictionary<string, object>();
            foreach (var key in groups) row[key] = first.TryGetValue(key, out var v) ? v ?? "" : "";
            foreach (var metric in metricSpecs)
            {
                // Format: function:field:alias  e.g. avg:ResolutionMinutes:AvgResolutionMinutes
                var parts = metric.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length < 2) continue;
                var fn = parts[0].ToLowerInvariant();
                var field = parts[1];
                var alias = parts.Length > 2 ? parts[2] : $"{fn}_{field}";
                var nums = g.Select(r => r.TryGetValue(field, out var v) && double.TryParse(v?.ToString(), out var n) ? (double?)n : null)
                    .Where(n => n.HasValue).Select(n => n!.Value).ToList();
                row[alias] = fn switch
                {
                    "count" => g.Count(),
                    "sum" => nums.Sum(),
                    "avg" or "average" => nums.Count > 0 ? nums.Average() : 0d,
                    "min" => nums.Count > 0 ? nums.Min() : 0d,
                    "max" => nums.Count > 0 ? nums.Max() : 0d,
                    _ => nums.Sum()
                };
            }
            output.Add(row);
        }
        return output;
    }

    private static List<Dictionary<string, object>> ApplyAppendRows(
        List<Dictionary<string, object>> rows,
        Dictionary<string, object> raw,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData)
    {
        var aliases = ReadStringList(raw, "sources");
        if (sourceData == null || aliases.Count == 0) return rows;
        foreach (var alias in aliases)
        {
            if (!sourceData.TryGetValue(alias, out var toAppend) || toAppend == null) continue;
            rows.AddRange(toAppend.Select(r => new Dictionary<string, object>(r, StringComparer.OrdinalIgnoreCase)));
        }
        return rows;
    }

    private static List<Dictionary<string, object>> ApplyMergeTables(
        List<Dictionary<string, object>> rows,
        Dictionary<string, object> raw,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData)
    {
        if (sourceData == null || sourceData.Count == 0) return rows;
        var leftAlias = ReadString(raw, "left_source", "left");
        var rightAlias = ReadString(raw, "right_source", "right");
        var leftRows = sourceData.TryGetValue(leftAlias, out var lRows) ? lRows : rows;
        var rightRows = sourceData.TryGetValue(rightAlias, out var rRows) ? rRows : rows;
        var leftKey = ReadString(raw, "left_key", "");
        var rightKey = ReadString(raw, "right_key", leftKey);
        if (string.IsNullOrWhiteSpace(leftKey) || string.IsNullOrWhiteSpace(rightKey)) return rows;
        var joinType = ReadString(raw, "join_type", "left").ToLowerInvariant();
        var rightLookup = rightRows
            .Where(r => r.TryGetValue(rightKey, out var rv) && rv != null)
            .GroupBy(r => r[rightKey]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var merged = new List<Dictionary<string, object>>();
        foreach (var left in leftRows)
        {
            var key = left.TryGetValue(leftKey, out var lv) ? lv?.ToString() ?? "" : "";
            if (rightLookup.TryGetValue(key, out var matches) && matches.Count > 0)
            {
                foreach (var right in matches)
                {
                    var row = new Dictionary<string, object>(left, StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in right)
                    {
                        var target = row.ContainsKey(kv.Key) ? $"{rightAlias}_{kv.Key}" : kv.Key;
                        row[target] = kv.Value;
                    }
                    merged.Add(row);
                }
            }
            else if (joinType is "left" or "full")
            {
                merged.Add(new Dictionary<string, object>(left, StringComparer.OrdinalIgnoreCase));
            }
        }

        if (joinType is "right" or "full")
        {
            var leftKeys = new HashSet<string>(leftRows
                .Select(r => r.TryGetValue(leftKey, out var lv) ? lv?.ToString() ?? "" : ""), StringComparer.OrdinalIgnoreCase);
            foreach (var right in rightRows)
            {
                var key = right.TryGetValue(rightKey, out var rv) ? rv?.ToString() ?? "" : "";
                if (leftKeys.Contains(key)) continue;
                merged.Add(new Dictionary<string, object>(right, StringComparer.OrdinalIgnoreCase));
            }
        }

        return merged;
    }

    private static List<Dictionary<string, object>> ApplyPivotTable(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var index = ReadString(raw, "index", "");
        var columns = ReadString(raw, "columns", "");
        var values = ReadString(raw, "values", "");
        if (string.IsNullOrWhiteSpace(index) || string.IsNullOrWhiteSpace(columns) || string.IsNullOrWhiteSpace(values))
            return rows;

        var pivot = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var indexValue = row.TryGetValue(index, out var iv) ? iv?.ToString() ?? "" : "";
            var colValue = row.TryGetValue(columns, out var cv) ? cv?.ToString() ?? "" : "";
            var cellValue = row.TryGetValue(values, out var vv) ? vv : null;
            if (!pivot.TryGetValue(indexValue, out var target))
            {
                target = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { [index] = indexValue };
                pivot[indexValue] = target;
            }
            target[colValue] = cellValue ?? "";
        }

        return pivot.Values.ToList();
    }

    private static List<Dictionary<string, object>> ApplyUnpivotTable(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var columns = ReadStringList(raw, "columns");
        var nameField = ReadString(raw, "name_field", "attribute");
        var valueField = ReadString(raw, "value_field", "value");
        if (columns.Count == 0) return rows;
        var output = new List<Dictionary<string, object>>();
        foreach (var row in rows)
        {
            var baseFields = row.Where(kv => !columns.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var c in columns)
            {
                var nr = new Dictionary<string, object>(baseFields, StringComparer.OrdinalIgnoreCase)
                {
                    [nameField] = c,
                    [valueField] = row.TryGetValue(c, out var v) ? v ?? "" : ""
                };
                output.Add(nr);
            }
        }
        return output;
    }

    private static List<Dictionary<string, object>> ApplyTransposeTable(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        if (rows.Count == 0) return rows;
        var keyColumn = ReadString(raw, "key_column", "Field");
        var valuePrefix = ReadString(raw, "value_prefix", "Row");
        var allColumns = rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var output = new List<Dictionary<string, object>>();
        foreach (var col in allColumns)
        {
            var nr = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { [keyColumn] = col };
            for (var i = 0; i < rows.Count; i++)
                nr[$"{valuePrefix}{i + 1}"] = rows[i].TryGetValue(col, out var v) ? v ?? "" : "";
            output.Add(nr);
        }
        return output;
    }

    private static List<Dictionary<string, object>> ApplyFilterRows(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var condition = ReadString(raw, "condition", "");
        if (string.IsNullOrWhiteSpace(condition)) return rows;
        return rows.Where(r => EvaluateCondition(condition, r)).ToList();
    }

    private static List<Dictionary<string, object>> ApplySortRows(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var by = ReadString(raw, "by", "");
        if (string.IsNullOrWhiteSpace(by)) return rows;
        var direction = ReadString(raw, "direction", "asc").ToLowerInvariant();
        return direction == "desc"
            ? rows.OrderByDescending(r => r.TryGetValue(by, out var v) ? v?.ToString() ?? "" : "", StringComparer.OrdinalIgnoreCase).ToList()
            : rows.OrderBy(r => r.TryGetValue(by, out var v) ? v?.ToString() ?? "" : "", StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<Dictionary<string, object>> ApplySelectColumns(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var include = ReadStringList(raw, "include");
        var exclude = ReadStringList(raw, "exclude");
        if (include.Count == 0 && exclude.Count == 0) return rows;
        return rows.Select(r =>
        {
            var output = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (include.Count > 0)
            {
                foreach (var c in include)
                    if (r.TryGetValue(c, out var v)) output[c] = v ?? "";
            }
            else
            {
                foreach (var kv in r)
                    if (!exclude.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                        output[kv.Key] = kv.Value;
            }
            return output;
        }).ToList();
    }

    private static void ApplyRenameColumns(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var mapping = ReadStringDictionary(raw, "mapping");
        if (mapping.Count == 0) return;
        foreach (var row in rows)
        {
            foreach (var (oldName, newName) in mapping)
            {
                if (string.IsNullOrWhiteSpace(newName) || !row.TryGetValue(oldName, out var value)) continue;
                row.Remove(oldName);
                row[newName] = value;
            }
        }
    }

    private static void ApplySplitColumn(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var field = ReadString(raw, "field", "");
        var delimiter = ReadString(raw, "delimiter", ",");
        var targets = ReadStringList(raw, "targets");
        if (string.IsNullOrWhiteSpace(field) || targets.Count == 0) return;
        foreach (var row in rows)
        {
            var input = row.TryGetValue(field, out var v) ? v?.ToString() ?? "" : "";
            var parts = input.Split(delimiter);
            for (var i = 0; i < targets.Count; i++)
                row[targets[i]] = i < parts.Length ? parts[i].Trim() : "";
        }
    }

    private static void ApplyReplaceValues(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var field = ReadString(raw, "field", "");
        var find = ReadString(raw, "find", "");
        var replace = ReadString(raw, "replace", "");
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(find)) return;
        foreach (var row in rows)
        {
            if (!row.TryGetValue(field, out var value) || value == null) continue;
            row[field] = (value.ToString() ?? "").Replace(find, replace, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void ApplyCastTypes(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var types = ReadStringDictionary(raw, "types");
        if (types.Count == 0) return;
        foreach (var row in rows)
        {
            foreach (var (field, targetType) in types)
            {
                if (!row.TryGetValue(field, out var value) || value == null) continue;
                var text = value.ToString() ?? "";
                row[field] = targetType.ToLowerInvariant() switch
                {
                    "int" or "integer" when int.TryParse(text, out var i) => i,
                    "decimal" or "number" when decimal.TryParse(text, out var d) => d,
                    "double" when double.TryParse(text, out var db) => db,
                    "bool" or "boolean" when bool.TryParse(text, out var b) => b,
                    "datetime" or "date" when DateTime.TryParse(text, out var dt) => dt,
                    _ => value
                };
            }
        }
    }

    private static void ApplyFlatten(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var field = ReadString(raw, "field", "");
        if (string.IsNullOrWhiteSpace(field)) return;
        var prefix = ReadString(raw, "prefix", field + "_");
        foreach (var row in rows)
        {
            if (!row.TryGetValue(field, out var v) || v == null) continue;
            var json = v.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                foreach (var prop in doc.RootElement.EnumerateObject())
                    row[prefix + prop.Name] = prop.Value.ToString();
            }
            catch
            {
                // Ignore invalid JSON and keep source value unchanged.
            }
        }
    }

    private static List<string> ValidateSchema(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var errors = new List<string>();
        var required = ReadStringList(raw, "required_fields");
        foreach (var field in required)
        {
            if (rows.Any(r => !r.ContainsKey(field) || r[field] == null || string.IsNullOrWhiteSpace(r[field]?.ToString())))
                errors.Add($"Required field '{field}' is missing/null.");
        }
        var types = ReadStringDictionary(raw, "types");
        foreach (var (field, expectedType) in types)
        {
            foreach (var row in rows)
            {
                if (!row.TryGetValue(field, out var v) || v == null) continue;
                var ok = expectedType.ToLowerInvariant() switch
                {
                    "number" => double.TryParse(v.ToString(), out _),
                    "datetime" or "date" => DateTime.TryParse(v.ToString(), out _),
                    "bool" or "boolean" => bool.TryParse(v.ToString(), out _),
                    _ => true
                };
                if (!ok) errors.Add($"Field '{field}' has invalid type. Expected {expectedType}.");
            }
        }
        var ranges = ReadStringDictionary(raw, "ranges");
        foreach (var (field, range) in ranges)
        {
            var parts = range.Split("..", StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !double.TryParse(parts[0], out var min) || !double.TryParse(parts[1], out var max)) continue;
            foreach (var row in rows)
            {
                if (!row.TryGetValue(field, out var v) || !double.TryParse(v?.ToString(), out var n)) continue;
                if (n < min || n > max) errors.Add($"Field '{field}' value {n} is out of range {min}..{max}.");
            }
        }
        return errors;
    }

    private static List<string> ValidateReferentialIntegrity(List<Dictionary<string, object>> rows, Dictionary<string, object> raw)
    {
        var errors = new List<string>();
        var field = ReadString(raw, "field", "");
        var allowed = new HashSet<string>(ReadStringList(raw, "allowed_values"), StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(field) || allowed.Count == 0) return errors;
        foreach (var row in rows)
        {
            if (!row.TryGetValue(field, out var v) || v == null) continue;
            var s = v.ToString() ?? "";
            if (!allowed.Contains(s))
                errors.Add($"Field '{field}' has value '{s}' which violates referential integrity.");
        }
        return errors;
    }

    private static object? EvaluateExpression(string expression, Dictionary<string, object> row, bool daxLike)
    {
        var expr = expression.Trim();
        if (daxLike && expr.StartsWith("IF(", StringComparison.OrdinalIgnoreCase))
        {
            var args = SplitTopLevelArgs(expr[3..^1]);
            if (args.Count == 3) return EvaluateCondition(args[0], row) ? EvaluateExpression(args[1], row, true) : EvaluateExpression(args[2], row, true);
        }
        if (daxLike && expr.StartsWith("DATEDIFF(", StringComparison.OrdinalIgnoreCase))
        {
            var args = SplitTopLevelArgs(expr[9..^1]);
            if (args.Count == 3)
            {
                var unit = args[0].Trim().Trim('"', '\'').ToLowerInvariant();
                var a = ToDate(EvaluateExpression(args[1], row, true));
                var b = ToDate(EvaluateExpression(args[2], row, true));
                if (a.HasValue && b.HasValue)
                {
                    var span = b.Value - a.Value;
                    return unit switch
                    {
                        "day" or "days" => Math.Round(span.TotalDays, 2),
                        "hour" or "hours" => Math.Round(span.TotalHours, 2),
                        _ => Math.Round(span.TotalMinutes, 2),
                    };
                }
            }
        }

        if (expr.StartsWith("[") && expr.EndsWith("]"))
        {
            var field = expr.Trim('[', ']');
            return row.TryGetValue(field, out var v) ? v : null;
        }

        var minusIndex = expr.IndexOf(" - ", StringComparison.Ordinal);
        if (minusIndex > 0)
        {
            var left = EvaluateExpression(expr[..minusIndex], row, daxLike);
            var right = EvaluateExpression(expr[(minusIndex + 3)..], row, daxLike);
            var leftDate = ToDate(left);
            var rightDate = ToDate(right);
            if (leftDate.HasValue && rightDate.HasValue) return Math.Round((leftDate.Value - rightDate.Value).TotalMinutes, 2);
            if (double.TryParse(left?.ToString(), out var l) && double.TryParse(right?.ToString(), out var r)) return l - r;
        }

        if (double.TryParse(expr, out var n)) return n;
        if ((expr.StartsWith('"') && expr.EndsWith('"')) || (expr.StartsWith('\'') && expr.EndsWith('\'')))
            return expr[1..^1];
        return expr;
    }

    private static bool EvaluateCondition(string condition, Dictionary<string, object> row)
    {
        var c = condition.Trim();
        var operators = new[] { ">=", "<=", "==", "!=", ">", "<" };
        foreach (var op in operators)
        {
            var idx = c.IndexOf(op, StringComparison.Ordinal);
            if (idx <= 0) continue;
            var left = EvaluateExpression(c[..idx], row, true);
            var right = EvaluateExpression(c[(idx + op.Length)..], row, true);
            var ls = left?.ToString() ?? "";
            var rs = right?.ToString() ?? "";
            if (double.TryParse(ls, out var ln) && double.TryParse(rs, out var rn))
            {
                return op switch
                {
                    ">=" => ln >= rn,
                    "<=" => ln <= rn,
                    ">" => ln > rn,
                    "<" => ln < rn,
                    "!=" => ln != rn,
                    _ => ln == rn
                };
            }
            return op switch
            {
                "!=" => !string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase)
            };
        }
        if (bool.TryParse(c, out var b)) return b;
        var val = EvaluateExpression(c, row, true);
        return val != null && !string.IsNullOrWhiteSpace(val.ToString());
    }

    private static List<string> SplitTopLevelArgs(string input)
    {
        var args = new List<string>();
        var depth = 0;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in input)
        {
            if (ch == ',' && depth == 0)
            {
                args.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            if (ch == '(') depth++;
            if (ch == ')') depth--;
            sb.Append(ch);
        }
        if (sb.Length > 0) args.Add(sb.ToString().Trim());
        return args;
    }

    private static DateTime? ToDate(object? o) =>
        DateTime.TryParse(o?.ToString(), out var dt) ? dt : null;

    private static int ScoreSentiment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var positive = new[] { "good", "great", "excellent", "happy", "resolved", "success" };
        var negative = new[] { "bad", "poor", "angry", "delay", "issue", "failed", "error" };
        var score = 0;
        foreach (var p in positive) if (text.Contains(p, StringComparison.OrdinalIgnoreCase)) score++;
        foreach (var n in negative) if (text.Contains(n, StringComparison.OrdinalIgnoreCase)) score--;
        return score;
    }

    private static string ToSnakeCase(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        var chars = new List<char>();
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsUpper(c) && i > 0) chars.Add('_');
            chars.Add(char.ToLowerInvariant(c == ' ' ? '_' : c));
        }
        return new string(chars.ToArray()).Replace("__", "_");
    }

    private static string ToPascalCase(string s) =>
        string.Concat((s ?? "").Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 1
                ? char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()
                : w.ToUpperInvariant()));

    private static string ToCamelCase(string s)
    {
        var pascal = ToPascalCase(s);
        if (string.IsNullOrEmpty(pascal)) return pascal;
        return pascal.Length == 1
            ? pascal.ToLowerInvariant()
            : char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    private static string ReadString(IDictionary<string, object> map, string key, string fallback)
    {
        if (!map.TryGetValue(key, out var value) || value == null) return fallback;
        var s = value.ToString();
        return string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
    }

    private static bool ReadBool(IDictionary<string, object> map, string key, bool fallback)
    {
        if (!map.TryGetValue(key, out var value) || value == null) return fallback;
        if (value is bool b) return b;
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private static List<string> ReadStringList(IDictionary<string, object> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value == null) return new List<string>();
        if (value is string s) return new List<string> { s };
        if (value is IEnumerable<object> arr) return arr.Select(a => a?.ToString() ?? "").Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return new List<string>();
    }

    private static Dictionary<string, string> ReadStringDictionary(IDictionary<string, object> map, string key)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!map.TryGetValue(key, out var value) || value == null) return result;
        if (value is IDictionary<string, object> dictObj)
        {
            foreach (var kv in dictObj) result[kv.Key] = kv.Value?.ToString() ?? "";
            return result;
        }
        return result;
    }
}
