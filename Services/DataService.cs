using AIInsights.Models;

namespace AIInsights.Services;

public class DataService : IDataService
{
    public List<string> GetDatasets() => new();
    public List<Dictionary<string, object>> GetData(string name) => new();
    public List<string> GetFields(string name) => new();
    public object GetAggregated(string datasetName, string labelField, string valueField, string aggregation) =>
        new { labels = Array.Empty<string>(), values = Array.Empty<double>() };
}
