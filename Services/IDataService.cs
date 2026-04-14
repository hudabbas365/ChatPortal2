namespace AIInsights.Services;

public interface IDataService
{
    List<string> GetDatasets();
    List<Dictionary<string, object>> GetData(string name);
    List<string> GetFields(string name);
    object GetAggregated(string datasetName, string labelField, string valueField, string aggregation);
}
