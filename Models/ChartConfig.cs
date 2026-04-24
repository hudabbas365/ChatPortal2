namespace AIInsights.Models;

public class ChartStyleConfig
{
    public string BackgroundColor { get; set; } = "#4A90D9";
    public string BorderColor { get; set; } = "#2C6FAC";
    public bool ShowLegend { get; set; } = true;
    public string LegendPosition { get; set; } = "top";
    public bool ShowTooltips { get; set; } = true;
    public bool FillArea { get; set; } = false;
    public string ColorPalette { get; set; } = "default";
    public bool ShowDataLabels { get; set; } = false;
    public string FontFamily { get; set; } = "Inter, sans-serif";
    public int TitleFontSize { get; set; } = 14;
    public bool Animated { get; set; } = true;
    public bool Responsive { get; set; } = true;
    public string BorderRadius { get; set; } = "4";

    // Card-level visual customization
    public string BoxShadow { get; set; } = "none"; // none | soft | medium | strong
    public string CardBackgroundColor { get; set; } = "";
    public string BackgroundImage { get; set; } = ""; // data URL (base64)
    public string IconImage { get; set; } = "";       // data URL (base64)
}

public class ChartConfig
{
    public string Type { get; set; } = "bar";
    public object Data { get; set; } = new();
    public object Options { get; set; } = new();
}

public class ChartTypeInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string Icon { get; set; } = "bi-bar-chart-fill";
    public string ChartJsType { get; set; } = "bar";
    public string Description { get; set; } = "";
}
