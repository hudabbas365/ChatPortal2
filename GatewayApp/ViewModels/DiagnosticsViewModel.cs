using System.Collections.ObjectModel;
using System.Windows.Input;
using GatewayApp.Models;
using GatewayApp.Services;

namespace GatewayApp.ViewModels;

public sealed class DiagnosticsViewModel : ViewModelBase
{
    private readonly DiagnosticsService _diagnosticsService;
    private DateTime? _startDate;
    private DateTime? _endDate;
    private string _statusFilter = "All";
    private string _datasourceFilter = "All";

    public DiagnosticsViewModel(DiagnosticsService diagnosticsService)
    {
        _diagnosticsService = diagnosticsService;
        Logs = new ObservableCollection<TransactionLog>();
        ApplyFilterCommand = new AsyncRelayCommand(_ => RefreshAsync());
    }

    public ObservableCollection<TransactionLog> Logs { get; }

    public DateTime? StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value);
    }

    public DateTime? EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value);
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set => SetProperty(ref _statusFilter, value);
    }

    public string DatasourceFilter
    {
        get => _datasourceFilter;
        set => SetProperty(ref _datasourceFilter, value);
    }

    public ICommand ApplyFilterCommand { get; }

    public async Task RefreshAsync()
    {
        Logs.Clear();
        var logs = await _diagnosticsService.GetLogsAsync();
        foreach (var log in logs.Where(FilterLog))
        {
            Logs.Add(log);
        }
    }

    private bool FilterLog(TransactionLog log)
    {
        var startOk = !StartDate.HasValue || log.Timestamp.Date >= StartDate.Value.Date;
        var endOk = !EndDate.HasValue || log.Timestamp.Date <= EndDate.Value.Date;
        var statusOk = StatusFilter == "All" || string.Equals(log.Status, StatusFilter, StringComparison.OrdinalIgnoreCase);
        var datasourceOk = DatasourceFilter == "All"
                           || string.Equals(log.DatasourceName, DatasourceFilter, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(log.DatasourceId, DatasourceFilter, StringComparison.OrdinalIgnoreCase);

        return startOk && endOk && statusOk && datasourceOk;
    }
}
