namespace ChatPortal2.Models;

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
    public string CustomJsonData { get; set; } = "";
}
