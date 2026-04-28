namespace AIInsights.Models;

public class FieldMapping
{
    public string LabelField { get; set; } = "";
    public string ValueField { get; set; } = "";
    public string GroupByField { get; set; } = "";
    public string XField { get; set; } = "";
    public string YField { get; set; } = "";
    public string RField { get; set; } = "";
    public List<string> MultiValueFields { get; set; } = new();
    // Per-field aggregation for the primary value field (e.g. SUM, AVG, COUNT,
    // COUNT_DISTINCT, MIN, MAX, STDEV, VAR, MEDIAN, or "None"). Persisted so
    // chart queries emit GROUP BY on reload — without this the value silently
    // drops on roundtrip and charts render raw, un-aggregated rows.
    public string ValueFieldAgg { get; set; } = "SUM";
    // Optional secondary value field used by combo/line-overlay charts.
    public string LineValueField { get; set; } = "";
    // Column list for the "table" chart type. Each entry carries the source
    // field name plus per-column display settings (label, width, visibility,
    // ordering). Persisted as objects — older dashboards that stored bare
    // strings are tolerated via the custom converter below.
    [Newtonsoft.Json.JsonConverter(typeof(TableFieldConfigListConverter))]
    public List<TableFieldConfig> TableFields { get; set; } = new();
}

public class TableFieldConfig
{
    public string FieldName { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Visible { get; set; } = true;
    public int? Width { get; set; }
}

// Tolerates legacy payloads where TableFields was a string[] — coerces each
// string into a TableFieldConfig{ FieldName = s } so old dashboards still load.
internal class TableFieldConfigListConverter : Newtonsoft.Json.JsonConverter<List<TableFieldConfig>>
{
    public override List<TableFieldConfig>? ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, List<TableFieldConfig>? existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (reader.TokenType == Newtonsoft.Json.JsonToken.Null) return new List<TableFieldConfig>();
        var arr = Newtonsoft.Json.Linq.JArray.Load(reader);
        var list = new List<TableFieldConfig>();
        foreach (var t in arr)
        {
            if (t.Type == Newtonsoft.Json.Linq.JTokenType.String)
            {
                var s = (string?)t ?? "";
                if (!string.IsNullOrWhiteSpace(s))
                    list.Add(new TableFieldConfig { FieldName = s, Label = s });
            }
            else if (t.Type == Newtonsoft.Json.Linq.JTokenType.Object)
            {
                var cfg = t.ToObject<TableFieldConfig>(serializer);
                if (cfg != null && !string.IsNullOrWhiteSpace(cfg.FieldName))
                    list.Add(cfg);
            }
        }
        return list;
    }

    public override void WriteJson(Newtonsoft.Json.JsonWriter writer, List<TableFieldConfig>? value, Newtonsoft.Json.JsonSerializer serializer)
    {
        serializer.Serialize(writer, value ?? new List<TableFieldConfig>());
    }
}
