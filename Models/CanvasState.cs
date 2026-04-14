using Newtonsoft.Json;
using System.Xml.Serialization;

namespace ChatPortal2.Models;

public class CanvasState
{
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<ReportPage> Pages { get; set; } = new() { new ReportPage { Name = "Page 1" } };
    public string CanvasName { get; set; } = "My Report";
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public int ActivePageIndex { get; set; } = 0;

    [JsonIgnore]
    [XmlIgnore]
    public List<ChartDefinition> Charts
    {
        get => Pages?.Count > 0 ? Pages[SafePageIndex].Charts : new();
        set { if (Pages?.Count > 0) Pages[SafePageIndex].Charts = value; }
    }

    private int SafePageIndex => Pages?.Count > 0
        ? Math.Clamp(ActivePageIndex, 0, Pages.Count - 1)
        : 0;
}

public class ReportPage
{
    public string Name { get; set; } = "Page 1";
    public List<ChartDefinition> Charts { get; set; } = new();
    public int PageWidth { get; set; } = 1200;
    public int PageHeight { get; set; } = 900;
    public string Background { get; set; } = "#ffffff";
}
