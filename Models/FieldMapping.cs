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
}
