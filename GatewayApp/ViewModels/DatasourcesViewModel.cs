using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using GatewayApp.Models;
using GatewayApp.Services;
using GatewayApp.Wizards;

namespace GatewayApp.ViewModels;

public sealed class DatasourcesViewModel : ViewModelBase
{
    private readonly DatasourceService _datasourceService;
    private readonly DiagnosticsService _diagnosticsService;

    public DatasourcesViewModel(DatasourceService datasourceService, DiagnosticsService diagnosticsService)
    {
        _datasourceService = datasourceService;
        _diagnosticsService = diagnosticsService;
        Connections = new ObservableCollection<DatasourceConnection>();
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        OpenWizardCommand = new RelayCommand(_ => OpenWizard());
    }

    public ObservableCollection<DatasourceConnection> Connections { get; }

    public ICommand RefreshCommand { get; }
    public ICommand OpenWizardCommand { get; }

    public async Task RefreshAsync()
    {
        Connections.Clear();
        var items = await _datasourceService.GetDatasourcesAsync();
        foreach (var item in items)
        {
            Connections.Add(item);
        }
    }

    private void OpenWizard()
    {
        var wizardVm = new ConnectionWizardViewModel(_datasourceService, _diagnosticsService);
        var wizard = new ConnectionWizardWindow { DataContext = wizardVm, Owner = Application.Current.MainWindow };
        if (wizard.ShowDialog() == true)
        {
            _ = RefreshAsync();
        }
    }
}
