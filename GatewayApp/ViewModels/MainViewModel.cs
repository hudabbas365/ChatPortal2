using System.Collections.ObjectModel;
using System.Windows.Input;
using GatewayApp.Services;

namespace GatewayApp.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public sealed record NavItem(string Name);

    private readonly SettingsViewModel _settingsViewModel;
    private readonly DatasourcesViewModel _datasourcesViewModel;
    private readonly DiagnosticsViewModel _diagnosticsViewModel;
    private object? _currentView;

    public MainViewModel()
    {
        var version = AppServices.Version.GetVersionInfo();
        Title = $"AI Insights 365 Gateway — v{version.Version}";

        Dashboard = new DashboardViewModel();
        _settingsViewModel = new SettingsViewModel(AppServices.Settings, AppServices.Gateway, AppServices.Auth, AppServices.Version);
        _datasourcesViewModel = new DatasourcesViewModel(AppServices.Datasource, AppServices.Diagnostics);
        _diagnosticsViewModel = new DiagnosticsViewModel(AppServices.Diagnostics);
        Recovery = new RecoveryViewModel(AppServices.Recovery);

        NavigationItems = new ObservableCollection<NavItem>
        {
            new("Dashboard"),
            new("Settings"),
            new("Datasources"),
            new("Diagnostics"),
            new("Recovery")
        };

        NavigateCommand = new AsyncRelayCommand(NavigateAsync);
        CurrentView = Dashboard;
    }

    public string Title { get; }
    public DashboardViewModel Dashboard { get; }
    public RecoveryViewModel Recovery { get; }

    public ObservableCollection<NavItem> NavigationItems { get; }

    public object? CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public ICommand NavigateCommand { get; }

    private async Task NavigateAsync(object? parameter)
    {
        if (parameter is not NavItem nav)
        {
            return;
        }

        switch (nav.Name)
        {
            case "Dashboard":
                CurrentView = Dashboard;
                break;
            case "Settings":
                await _settingsViewModel.InitializeAsync();
                CurrentView = _settingsViewModel;
                break;
            case "Datasources":
                await _datasourcesViewModel.RefreshAsync();
                CurrentView = _datasourcesViewModel;
                break;
            case "Diagnostics":
                await _diagnosticsViewModel.RefreshAsync();
                CurrentView = _diagnosticsViewModel;
                break;
            case "Recovery":
                CurrentView = Recovery;
                break;
        }
    }
}
