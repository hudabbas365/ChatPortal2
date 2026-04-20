using System.Windows;
using System.Windows.Input;
using GatewayApp.Models;
using GatewayApp.Services;

namespace GatewayApp.ViewModels;

public sealed class ConnectionWizardViewModel : ViewModelBase
{
    private readonly DatasourceService _datasourceService;
    private readonly DiagnosticsService _diagnosticsService;
    private int _stepIndex;
    private string _connectionType = "OnPremises";
    private string _name = string.Empty;
    private string _host = string.Empty;
    private string _port = "1433";
    private string _database = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _status = string.Empty;

    public ConnectionWizardViewModel(DatasourceService datasourceService, DiagnosticsService diagnosticsService)
    {
        _datasourceService = datasourceService;
        _diagnosticsService = diagnosticsService;
        NextCommand = new AsyncRelayCommand(_ => NextAsync());
        BackCommand = new RelayCommand(_ => StepIndex = Math.Max(0, StepIndex - 1));
        CancelCommand = new RelayCommand(_ => CloseDialog(false));
        StepIndex = 0;
    }

    public int StepIndex
    {
        get => _stepIndex;
        set => SetProperty(ref _stepIndex, value);
    }

    public string ConnectionType
    {
        get => _connectionType;
        set => SetProperty(ref _connectionType, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    public string Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public string Database
    {
        get => _database;
        set => SetProperty(ref _database, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task NextAsync()
    {
        if (StepIndex < 2)
        {
            StepIndex++;
            return;
        }

        var conn = new DatasourceConnection
        {
            Name = Name,
            Type = ConnectionType,
            ConnectionString = $"Server={Host},{Port};Database={Database};User Id={Username};Password={Password};"
        };

        var ok = await _datasourceService.TestConnectionAsync(conn);
        if (!ok)
        {
            Status = "Connection test failed.";
            return;
        }

        var added = await _datasourceService.AddDatasourceAsync(conn);
        Status = added ? "Datasource saved." : "Could not save datasource.";
        _diagnosticsService.LogTransaction(new Models.TransactionLog
        {
            Query = "Datasource registration",
            DatasourceId = conn.Id,
            DatasourceName = conn.Name,
            Status = added ? "Success" : "Failed"
        });

        if (added)
        {
            CloseDialog(true);
        }
    }

    private static void CloseDialog(bool? result)
    {
        var window = Application.Current.Windows.OfType<Window>().SingleOrDefault(w => w.IsActive);
        if (window is not null)
        {
            window.DialogResult = result;
            window.Close();
        }
    }
}
