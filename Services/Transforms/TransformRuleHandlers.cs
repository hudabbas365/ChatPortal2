namespace AIInsights.Services.Transforms;

// ─── Shared helper base ───────────────────────────────────────────────────────

/// <summary>
/// Common read helpers shared by all rule handlers, extracted from
/// <see cref="AiInsightTransformService"/> to avoid duplication.
/// </summary>
internal static class RuleHelpers
{
    public static string ReadString(IDictionary<string, object> map, string key, string fallback = "")
    {
        if (!map.TryGetValue(key, out var value) || value == null) return fallback;
        var s = value.ToString();
        return string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
    }

    public static bool ReadBool(IDictionary<string, object> map, string key, bool fallback)
    {
        if (!map.TryGetValue(key, out var value) || value == null) return fallback;
        if (value is bool b) return b;
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    public static List<string> ReadStringList(IDictionary<string, object> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value == null) return new List<string>();
        if (value is string s) return new List<string> { s };
        if (value is IEnumerable<object> arr)
            return arr.Select(a => a?.ToString() ?? "").Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return new List<string>();
    }

    public static Dictionary<string, string> ReadStringDictionary(IDictionary<string, object> map, string key)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!map.TryGetValue(key, out var value) || value == null) return result;
        if (value is IDictionary<string, object> dictObj)
        {
            foreach (var kv in dictObj) result[kv.Key] = kv.Value?.ToString() ?? "";
        }
        return result;
    }

    public static object? EvaluateExpression(string expression, Dictionary<string, object> row, bool daxLike)
    {
        var expr = expression.Trim();
        if (daxLike && expr.StartsWith("IF(", StringComparison.OrdinalIgnoreCase))
        {
            var args = SplitTopLevelArgs(expr[3..^1]);
            if (args.Count == 3)
                return EvaluateCondition(args[0], row)
                    ? EvaluateExpression(args[1], row, true)
                    : EvaluateExpression(args[2], row, true);
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
            if (leftDate.HasValue && rightDate.HasValue)
                return Math.Round((leftDate.Value - rightDate.Value).TotalMinutes, 2);
            if (double.TryParse(left?.ToString(), out var l) && double.TryParse(right?.ToString(), out var r))
                return l - r;
        }

        if (double.TryParse(expr, out var n)) return n;
        if ((expr.StartsWith('"') && expr.EndsWith('"')) || (expr.StartsWith('\'') && expr.EndsWith('\'')))
            return expr[1..^1];
        return expr;
    }

    public static bool EvaluateCondition(string condition, Dictionary<string, object> row)
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

    public static List<string> SplitTopLevelArgs(string input)
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

    public static DateTime? ToDate(object? o) =>
        DateTime.TryParse(o?.ToString(), out var dt) ? dt : null;

    public static string ToSnakeCase(string s)
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

    public static string ToPascalCase(string s) =>
        string.Concat((s ?? "").Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 1
                ? char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()
                : w.ToUpperInvariant()));

    public static string ToCamelCase(string s)
    {
        var pascal = ToPascalCase(s);
        if (string.IsNullOrEmpty(pascal)) return pascal;
        return pascal.Length == 1
            ? pascal.ToLowerInvariant()
            : char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    public static int ScoreSentiment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var positive = new[] { "good", "great", "excellent", "happy", "resolved", "success" };
        var negative = new[] { "bad", "poor", "angry", "delay", "issue", "failed", "error" };
        var score = 0;
        foreach (var p in positive) if (text.Contains(p, StringComparison.OrdinalIgnoreCase)) score++;
        foreach (var n in negative) if (text.Contains(n, StringComparison.OrdinalIgnoreCase)) score--;
        return score;
    }
}

// ─── Clean group ─────────────────────────────────────────────────────────────

public sealed class RemoveDuplicatesHandler : ITransformRuleHandler
{
    public string Type => "remove_duplicates";
    public string Group => "Clean";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var keys = RuleHelpers.ReadStringList(raw, "keys");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outRows = new List<Dictionary<string, object>>();
        foreach (var row in rows)
        {
            var sig = keys.Count > 0
                ? string.Join("|", keys.Select(k => (row.TryGetValue(k, out var v) ? v : null)?.ToString() ?? ""))
                : string.Join("|", row.OrderBy(k => k.Key).Select(kv => $"{kv.Key}={kv.Value}"));
            if (seen.Add(sig)) outRows.Add(row);
        }
        audit.Add("Data cleansing: removed duplicates.");
        return outRows;
    }
}

public sealed class HandleNullsHandler : ITransformRuleHandler
{
    public string Type => "handle_nulls";
    public string Group => "Clean";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var fields = RuleHelpers.ReadStringList(raw, "fields");
        var oneField = RuleHelpers.ReadString(raw, "field");
        if (!string.IsNullOrWhiteSpace(oneField)) fields.Add(oneField);
        if (fields.Count == 0)
            fields = rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var strategy = RuleHelpers.ReadString(raw, "strategy", "replace").ToLowerInvariant();
        var replacement = RuleHelpers.ReadString(raw, "value");
        foreach (var row in rows.ToArray())
        {
            var drop = false;
            foreach (var f in fields)
            {
                row.TryGetValue(f, out var v);
                var isMissing = v == null
                    || string.IsNullOrWhiteSpace(v.ToString())
                    || string.Equals(v.ToString(), "NULL", StringComparison.OrdinalIgnoreCase);
                if (!isMissing) continue;
                if (strategy == "drop_row") { drop = true; break; }
                row[f] = replacement;
            }
            if (drop) rows.Remove(row);
        }
        audit.Add("Data cleansing: handled null/missing values.");
        return rows;
    }
}

public sealed class ReplaceValuesHandler : ITransformRuleHandler
{
    public string Type => "replace_values";
    public string Group => "Clean";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var field = RuleHelpers.ReadString(raw, "field");
        var find = RuleHelpers.ReadString(raw, "find");
        var replace = RuleHelpers.ReadString(raw, "replace");
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(find)) return rows;
        foreach (var row in rows)
        {
            if (!row.TryGetValue(field, out var value) || value == null) continue;
            row[field] = (value.ToString() ?? "").Replace(find, replace, StringComparison.OrdinalIgnoreCase);
        }
        audit.Add("Clean: replace values applied.");
        return rows;
    }
}

public sealed class SplitColumnHandler : ITransformRuleHandler
{
    public string Type => "split_column";
    public string Group => "Clean";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var field = RuleHelpers.ReadString(raw, "field");
        var delimiter = RuleHelpers.ReadString(raw, "delimiter", ",");
        var targets = RuleHelpers.ReadStringList(raw, "targets");
        if (string.IsNullOrWhiteSpace(field) || targets.Count == 0) return rows;
        foreach (var row in rows)
        {
            var input = row.TryGetValue(field, out var v) ? v?.ToString() ?? "" : "";
            var parts = input.Split(delimiter);
            for (var i = 0; i < targets.Count; i++)
                row[targets[i]] = i < parts.Length ? parts[i].Trim() : "";
        }
        audit.Add("Clean: split column applied.");
        return rows;
    }
}

public sealed class StandardizeFormatHandler : ITransformRuleHandler
{
    public string Type => "standardize_format";
    public string Group => "Clean";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var field = RuleHelpers.ReadString(raw, "field");
        if (string.IsNullOrWhiteSpace(field)) return rows;
        var format = RuleHelpers.ReadString(raw, "format", "text").ToLowerInvariant();
        var output = RuleHelpers.ReadString(raw, "output", "yyyy-MM-dd");
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
        audit.Add("Data cleansing: standardized value formats.");
        return rows;
    }
}

// ─── Shape group ──────────────────────────────────────────────────────────────

public sealed class FilterRowsHandler : ITransformRuleHandler
{
    public string Type => "filter_rows";
    public string Group => "Shape";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var condition = RuleHelpers.ReadString(raw, "condition");
        if (string.IsNullOrWhiteSpace(condition)) return rows;
        var result = rows.Where(r => RuleHelpers.EvaluateCondition(condition, r)).ToList();
        audit.Add("Shape: filter rows applied.");
        return result;
    }
}

public sealed class SortRowsHandler : ITransformRuleHandler
{
    public string Type => "sort_rows";
    public string Group => "Shape";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var by = RuleHelpers.ReadString(raw, "by");
        if (string.IsNullOrWhiteSpace(by)) return rows;
        var direction = RuleHelpers.ReadString(raw, "direction", "asc").ToLowerInvariant();
        var result = direction == "desc"
            ? rows.OrderByDescending(r => r.TryGetValue(by, out var v) ? v?.ToString() ?? "" : "", StringComparer.OrdinalIgnoreCase).ToList()
            : rows.OrderBy(r => r.TryGetValue(by, out var v) ? v?.ToString() ?? "" : "", StringComparer.OrdinalIgnoreCase).ToList();
        audit.Add("Shape: sort rows applied.");
        return result;
    }
}

public sealed class SelectColumnsHandler : ITransformRuleHandler
{
    public string Type => "select_columns";
    public string Group => "Shape";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var include = RuleHelpers.ReadStringList(raw, "include");
        var exclude = RuleHelpers.ReadStringList(raw, "exclude");
        if (include.Count == 0 && exclude.Count == 0) return rows;
        var result = rows.Select(r =>
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
        audit.Add("Shape: select columns applied.");
        return result;
    }
}

public sealed class RenameColumnsHandler : ITransformRuleHandler
{
    public string Type => "rename_columns";
    public string Group => "Shape";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var mapping = RuleHelpers.ReadStringDictionary(raw, "mapping");
        if (mapping.Count == 0) return rows;
        foreach (var row in rows)
        {
            foreach (var (oldName, newName) in mapping)
            {
                if (string.IsNullOrWhiteSpace(newName) || !row.TryGetValue(oldName, out var value)) continue;
                row.Remove(oldName);
                row[newName] = value;
            }
        }
        audit.Add("Shape: rename columns applied.");
        return rows;
    }
}

public sealed class CastTypesHandler : ITransformRuleHandler
{
    public string Type => "cast_types";
    public string Group => "Shape";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var types = RuleHelpers.ReadStringDictionary(raw, "types");
        if (types.Count == 0) return rows;
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
        audit.Add("Validate: cast types applied.");
        return rows;
    }
}

public sealed class PivotTableHandler : ITransformRuleHandler
{
    public string Type => "pivot_table";
    public string Group => "Shape";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var index = RuleHelpers.ReadString(raw, "index");
        var columns = RuleHelpers.ReadString(raw, "columns");
        var values = RuleHelpers.ReadString(raw, "values");
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
        audit.Add("Shape: pivot table applied.");
        return pivot.Values.ToList();
    }
}

public sealed class UnpivotTableHandler : ITransformRuleHandler
{
    public string Type => "unpivot_table";
    public string Group => "Shape";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var columns = RuleHelpers.ReadStringList(raw, "columns");
        var nameField = RuleHelpers.ReadString(raw, "name_field", "attribute");
        var valueField = RuleHelpers.ReadString(raw, "value_field", "value");
        if (columns.Count == 0) return rows;
        var output = new List<Dictionary<string, object>>();
        foreach (var row in rows)
        {
            var baseFields = row
                .Where(kv => !columns.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
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
        audit.Add("Shape: unpivot table applied.");
        return output;
    }
}

public sealed class TransposeTableHandler : ITransformRuleHandler
{
    public string Type => "transpose_table";
    public string Group => "Shape";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        if (rows.Count == 0) return rows;
        var keyColumn = RuleHelpers.ReadString(raw, "key_column", "Field");
        var valuePrefix = RuleHelpers.ReadString(raw, "value_prefix", "Row");
        var allColumns = rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var output = new List<Dictionary<string, object>>();
        foreach (var col in allColumns)
        {
            var nr = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { [keyColumn] = col };
            for (var i = 0; i < rows.Count; i++)
                nr[$"{valuePrefix}{i + 1}"] = rows[i].TryGetValue(col, out var v) ? v ?? "" : "";
            output.Add(nr);
        }
        audit.Add("Shape: transpose table applied.");
        return output;
    }
}

// ─── Combine group ────────────────────────────────────────────────────────────

public sealed class AppendRowsHandler : ITransformRuleHandler
{
    public string Type => "append_rows";
    public string Group => "Combine";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var aliases = RuleHelpers.ReadStringList(raw, "sources");
        if (sourceData == null || aliases.Count == 0) return rows;
        foreach (var alias in aliases)
        {
            if (!sourceData.TryGetValue(alias, out var toAppend) || toAppend == null) continue;
            rows.AddRange(toAppend.Select(r => new Dictionary<string, object>(r, StringComparer.OrdinalIgnoreCase)));
        }
        audit.Add("Combine: rows appended from selected source tables.");
        return rows;
    }
}

public sealed class MergeTablesHandler : ITransformRuleHandler
{
    public string Type => "merge_tables";
    public string Group => "Combine";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var leftKey = RuleHelpers.ReadString(raw, "left_key");
        var rightKey = RuleHelpers.ReadString(raw, "right_key", leftKey);
        if (string.IsNullOrWhiteSpace(leftKey) || string.IsNullOrWhiteSpace(rightKey))
        {
            audit.Add("Combine warning: merge_tables skipped because left_key/right_key were not provided.");
            return rows;
        }

        if (sourceData == null || sourceData.Count == 0) return rows;

        var leftAlias = RuleHelpers.ReadString(raw, "left_source", "left");
        var rightAlias = RuleHelpers.ReadString(raw, "right_source", "right");

        var hasLeft = sourceData.TryGetValue(leftAlias, out var lRows);
        var hasRight = sourceData.TryGetValue(rightAlias, out var rRows);

        // Bug fix: when neither alias is in sourceData both would fall back to `rows`,
        // producing a silent self-join. Detect and skip with a warning instead.
        if (!hasLeft && !hasRight)
        {
            audit.Add($"Combine warning: merge_tables skipped — neither source '{leftAlias}' nor '{rightAlias}' found in provided source data.");
            return rows;
        }

        var leftRows = hasLeft ? lRows! : rows;
        var rightRows = hasRight ? rRows! : rows;

        var joinType = RuleHelpers.ReadString(raw, "join_type", "left").ToLowerInvariant();
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
            var leftKeys = new HashSet<string>(
                leftRows.Select(r => r.TryGetValue(leftKey, out var lv) ? lv?.ToString() ?? "" : ""),
                StringComparer.OrdinalIgnoreCase);
            foreach (var right in rightRows)
            {
                var key = right.TryGetValue(rightKey, out var rv) ? rv?.ToString() ?? "" : "";
                if (leftKeys.Contains(key)) continue;
                merged.Add(new Dictionary<string, object>(right, StringComparer.OrdinalIgnoreCase));
            }
        }

        audit.Add("Combine: table merge completed.");
        return merged;
    }
}

// ─── Aggregate group ──────────────────────────────────────────────────────────

public sealed class AggregateHandler : ITransformRuleHandler
{
    public string Type => "aggregate";
    public string Group => "Aggregate";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var groups = RuleHelpers.ReadStringList(raw, "group_by");
        var metricSpecs = RuleHelpers.ReadStringList(raw, "metrics");
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
                var parts = metric.Split(':', StringSplitOptions.TrimEntries);
                if (parts.Length < 2) continue;
                var fn = parts[0].ToLowerInvariant();
                var field = parts[1];
                var alias = parts.Length > 2 ? parts[2] : $"{fn}_{field}";
                var nums = g
                    .Select(r => r.TryGetValue(field, out var v) && double.TryParse(v?.ToString(), out var n) ? (double?)n : null)
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
        audit.Add("Aggregation: grouped and summarized.");
        return output;
    }
}

// ─── Validate group ───────────────────────────────────────────────────────────

public sealed class ValidateSchemaHandler : ITransformRuleHandler
{
    public string Type => "validate_schema";
    public string Group => "Validate";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var errors = new List<string>();
        var required = RuleHelpers.ReadStringList(raw, "required_fields");
        foreach (var field in required)
        {
            if (rows.Any(r => !r.ContainsKey(field) || r[field] == null || string.IsNullOrWhiteSpace(r[field]?.ToString())))
                errors.Add($"Required field '{field}' is missing/null.");
        }
        var types = RuleHelpers.ReadStringDictionary(raw, "types");
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
        var ranges = RuleHelpers.ReadStringDictionary(raw, "ranges");
        foreach (var (field, range) in ranges)
        {
            var parts = range.Split("..", StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !double.TryParse(parts[0], out var min) || !double.TryParse(parts[1], out var max)) continue;
            foreach (var row in rows)
            {
                if (!row.TryGetValue(field, out var v) || !double.TryParse(v?.ToString(), out var n)) continue;
                if (n < min || n > max)
                    errors.Add($"Field '{field}' value {n} is out of range {min}..{max}.");
            }
        }

        if (errors.Count > 0)
        {
            var strict = RuleHelpers.ReadBool(raw, "strict", true);
            foreach (var e in errors) audit.Add("Validation: " + e);
            if (strict)
            {
                var errSummary = "Transform validation failed: " + string.Join(" | ", errors.Take(5));
                throw new TransformAbortException(errSummary, errSummary);
            }
        }
        else
        {
            audit.Add("Validation: schema checks passed.");
        }
        return rows;
    }
}

public sealed class ReferentialIntegrityHandler : ITransformRuleHandler
{
    public string Type => "referential_integrity";
    public string Group => "Validate";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var errors = new List<string>();
        var field = RuleHelpers.ReadString(raw, "field");
        var allowed = new HashSet<string>(RuleHelpers.ReadStringList(raw, "allowed_values"), StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(field) || allowed.Count == 0) return rows;
        foreach (var row in rows)
        {
            if (!row.TryGetValue(field, out var v) || v == null) continue;
            var s = v.ToString() ?? "";
            if (!allowed.Contains(s))
                errors.Add($"Field '{field}' has value '{s}' which violates referential integrity.");
        }

        if (errors.Count > 0)
        {
            foreach (var e in errors) audit.Add("Referential integrity: " + e);
            if (RuleHelpers.ReadBool(raw, "strict", true))
            {
                const string errMsg = "Transform referential integrity checks failed.";
                throw new TransformAbortException(errMsg, errMsg);
            }
        }
        else
        {
            audit.Add("Validation: referential integrity checks passed.");
        }
        return rows;
    }
}

// ─── AI Assist group ──────────────────────────────────────────────────────────

public sealed class DerivedFieldHandler : ITransformRuleHandler
{
    public string Type => "derived_field";
    public string Group => "AI Assist";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var target = RuleHelpers.ReadString(raw, "target_field");
        var expr = RuleHelpers.ReadString(raw, "expression");
        // dax_like_expressions defaults to true; callers may pass it via raw or it is read from the parent definition
        var daxLike = RuleHelpers.ReadBool(raw, "dax_like_expressions", true);
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(expr)) return rows;
        foreach (var row in rows)
            row[target] = RuleHelpers.EvaluateExpression(expr, row, daxLike) ?? "NULL";
        audit.Add("Business logic: derived field computed.");
        return rows;
    }
}

// ─── Normalize group ──────────────────────────────────────────────────────────

public sealed class ConvertUnitsHandler : ITransformRuleHandler
{
    public string Type => "convert_units";
    public string Group => "Normalize";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var field = RuleHelpers.ReadString(raw, "field");
        if (string.IsNullOrWhiteSpace(field)) return rows;
        var target = RuleHelpers.ReadString(raw, "target_field", field);
        var from = RuleHelpers.ReadString(raw, "from").ToUpperInvariant();
        var to = RuleHelpers.ReadString(raw, "to").ToUpperInvariant();
        foreach (var row in rows)
        {
            if (!row.TryGetValue(field, out var v) || !double.TryParse(v?.ToString(), out var n)) continue;
            if (from == "MB" && to == "GB") n /= 1024d;
            else if (from == "GB" && to == "MB") n *= 1024d;
            else if (from == "SECONDS" && to == "MINUTES") n /= 60d;
            else if (from == "MINUTES" && to == "HOURS") n /= 60d;
            row[target] = Math.Round(n, 4);
        }
        audit.Add("Normalization: unit conversion applied.");
        return rows;
    }
}

public sealed class ApplyNamingConventionHandler : ITransformRuleHandler
{
    public string Type => "apply_naming_convention";
    public string Group => "Normalize";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var style = RuleHelpers.ReadString(raw, "style", "snake_case").ToLowerInvariant();
        var fields = RuleHelpers.ReadStringList(raw, "fields");
        if (fields.Count == 0)
            fields = rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
                    "pascalcase" => RuleHelpers.ToPascalCase(f),
                    "camelcase" => RuleHelpers.ToCamelCase(f),
                    _ => RuleHelpers.ToSnakeCase(f),
                };
                updates[renamed] = v;
                if (!renamed.Equals(f, StringComparison.OrdinalIgnoreCase)) row.Remove(f);
            }
            foreach (var kv in updates) row[kv.Key] = kv.Value;
        }
        audit.Add("Normalization: naming convention applied.");
        return rows;
    }
}

public sealed class MapCodesHandler : ITransformRuleHandler
{
    public string Type => "map_codes";
    public string Group => "Normalize";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var field = RuleHelpers.ReadString(raw, "field");
        if (string.IsNullOrWhiteSpace(field)) return rows;
        var target = RuleHelpers.ReadString(raw, "target_field", field);
        var mapping = RuleHelpers.ReadStringDictionary(raw, "mapping");
        if (mapping.Count == 0) return rows;
        foreach (var row in rows)
        {
            if (!row.TryGetValue(field, out var v)) continue;
            var key = (v?.ToString() ?? "").Trim();
            if (mapping.TryGetValue(key, out var mapped)) row[target] = mapped;
        }
        audit.Add("Normalization: code mapping applied.");
        return rows;
    }
}

// ─── Business Logic group ─────────────────────────────────────────────────────

public sealed class KpiClassificationHandler : ITransformRuleHandler
{
    public string Type => "kpi_classification";
    public string Group => "Business Logic";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var field = RuleHelpers.ReadString(raw, "field");
        if (string.IsNullOrWhiteSpace(field)) return rows;
        var target = RuleHelpers.ReadString(raw, "target_field", "KpiClass");
        var fr = new HashSet<string>(RuleHelpers.ReadStringList(raw, "fr_values"), StringComparer.OrdinalIgnoreCase);
        var notFr = new HashSet<string>(RuleHelpers.ReadStringList(raw, "not_fr_values"), StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var val = row.TryGetValue(field, out var v) ? (v?.ToString() ?? "") : "";
            if (fr.Contains(val)) row[target] = "FR";
            else if (notFr.Contains(val)) row[target] = "Not FR";
            else row[target] = "N/A";
        }
        audit.Add("Business logic: KPI classification applied.");
        return rows;
    }
}

public sealed class SentimentScoreHandler : ITransformRuleHandler
{
    public string Type => "sentiment_score";
    public string Group => "Business Logic";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var field = RuleHelpers.ReadString(raw, "field");
        if (string.IsNullOrWhiteSpace(field)) return rows;
        var target = RuleHelpers.ReadString(raw, "target_field", "SentimentScore");
        foreach (var row in rows)
        {
            var text = row.TryGetValue(field, out var v) ? (v?.ToString() ?? "") : "";
            row[target] = RuleHelpers.ScoreSentiment(text);
        }
        audit.Add("Business logic: sentiment score computed.");
        return rows;
    }
}

// ─── Restructure group ────────────────────────────────────────────────────────

public sealed class FlattenJsonHandler : ITransformRuleHandler
{
    public string Type => "flatten_json";
    public string Group => "Restructure";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        var field = RuleHelpers.ReadString(raw, "field");
        if (string.IsNullOrWhiteSpace(field)) return rows;
        var prefix = RuleHelpers.ReadString(raw, "prefix", field + "_");
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
        audit.Add("Restructuring: flattened nested JSON.");
        return rows;
    }
}

// ─── Audit group ──────────────────────────────────────────────────────────────

public sealed class LogTransformationsHandler : ITransformRuleHandler
{
    public string Type => "log_transformations";
    public string Group => "Audit";

    public List<Dictionary<string, object>> Apply(
        Dictionary<string, object> raw,
        List<Dictionary<string, object>> rows,
        Dictionary<string, List<Dictionary<string, object>>>? sourceData,
        IList<string> audit)
    {
        audit.Add("Audit: explicit log_transformations marker reached.");
        return rows;
    }
}
