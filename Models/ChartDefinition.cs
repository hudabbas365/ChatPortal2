namespace AIInsights.Models;

public class ChartDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string ChartType { get; set; } = "bar";
    public string Title { get; set; } = "New Chart";
    public int GridCol { get; set; } = 0;
    public int GridRow { get; set; } = 0;
    public int Width { get; set; } = 4;
    public int Height { get; set; } = 300;
    public int PosX { get; set; } = 20;
    public int PosY { get; set; } = 20;
    public int ZIndex { get; set; } = 1;
    public FieldMapping Mapping { get; set; } = new();
    public AggregationConfig Aggregation { get; set; } = new();
    public ChartStyleConfig Style { get; set; } = new();
    public string DatasetName { get; set; } = "sales";
    public string? DatasourceId { get; set; }
    public string? DataQuery { get; set; }
    public string CustomJsonData { get; set; } = "";
    public int RowLimit { get; set; } = 15;
    public string FilterWhere { get; set; } = "";
    public ShapeProperties? ShapeProps { get; set; }
    public string? GroupId { get; set; }
}

public class ShapeProperties
{
    public string FillColor { get; set; } = "#5B9BD5";
    public string StrokeColor { get; set; } = "#3A7BBF";
    public int StrokeWidth { get; set; } = 2;
    public double Opacity { get; set; } = 1;
    public string Text { get; set; } = "";
    public int FontSize { get; set; } = 16;
    public string FontColor { get; set; } = "#1E2D3D";
    public string TextAlign { get; set; } = "center";
    public string FontWeight { get; set; } = "normal";
    public int CornerRadius { get; set; } = 0;
}
