using ChatPortal2.Models;
using Newtonsoft.Json;

namespace ChatPortal2.Services;

public class DataService : IDataService
{
    private readonly Dictionary<string, List<Dictionary<string, object>>> _cache = new();
    private readonly IWebHostEnvironment _env;

    public DataService(IWebHostEnvironment env)
    {
        _env = env;
        LoadAll();
    }

    private void LoadAll()
    {
        var datasets = new Dictionary<string, string>
        {
            ["sales"] = SampleDataSets.Sales,
            ["population"] = SampleDataSets.Population,
            ["weather"] = SampleDataSets.Weather,
            ["finance"] = SampleDataSets.Finance
        };

        foreach (var kv in datasets)
        {
            try
            {
                var dataPath = Path.Combine(_env.WebRootPath, "data", $"{kv.Key}.json");
                string json;
                if (File.Exists(dataPath))
                    json = File.ReadAllText(dataPath);
                else
                    json = kv.Value;

                var list = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json)
                           ?? new List<Dictionary<string, object>>();
                _cache[kv.Key] = list;
            }
            catch
            {
                _cache[kv.Key] = new List<Dictionary<string, object>>();
            }
        }
    }

    public List<string> GetDatasets() => _cache.Keys.ToList();

    public List<Dictionary<string, object>> GetData(string name)
    {
        var key = name?.ToLower() ?? "sales";
        return _cache.TryGetValue(key, out var data) ? data : new List<Dictionary<string, object>>();
    }

    public List<string> GetFields(string name)
    {
        var data = GetData(name);
        if (!data.Any()) return new List<string>();
        return data[0].Keys.ToList();
    }

    public object GetAggregated(string datasetName, string labelField, string valueField, string aggregation)
    {
        var data = GetData(datasetName);
        if (!data.Any()) return new { labels = Array.Empty<string>(), values = Array.Empty<double>() };

        var groups = data
            .Where(r => r.ContainsKey(labelField) && r.ContainsKey(valueField))
            .GroupBy(r => r[labelField]?.ToString() ?? "")
            .ToList();

        var labels = groups.Select(g => g.Key).ToArray();
        var values = groups.Select(g =>
        {
            var nums = g.Select(r =>
            {
                if (double.TryParse(r[valueField]?.ToString(), out double v)) return v;
                return 0.0;
            }).ToList();

            return aggregation?.ToUpper() switch
            {
                "SUM" => nums.Sum(),
                "AVG" => nums.Average(),
                "COUNT" => (double)nums.Count,
                "MAX" => nums.Max(),
                "MIN" => nums.Min(),
                _ => nums.Sum()
            };
        }).ToArray();

        return new { labels, values };
    }
}
