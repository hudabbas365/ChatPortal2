using ChatPortal2.Models;

namespace ChatPortal2.Services;

public interface IChartService
{
    List<ChartTypeInfo> GetChartLibrary();
    List<ChartDefinition> GetDefaultCharts();
    IEnumerable<IGrouping<string, ChartTypeInfo>> GetGroupedCharts();
}
