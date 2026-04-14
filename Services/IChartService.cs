using AIInsights.Models;

namespace AIInsights.Services;

public interface IChartService
{
    List<ChartTypeInfo> GetChartLibrary();
    List<ChartDefinition> GetDefaultCharts();
    IEnumerable<IGrouping<string, ChartTypeInfo>> GetGroupedCharts();
}
