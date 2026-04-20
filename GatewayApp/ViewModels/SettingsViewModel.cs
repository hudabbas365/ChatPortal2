using System.Windows.Input;
using GatewayApp.Models;
using GatewayApp.Services;

namespace GatewayApp.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly GatewaySettingsService _settingsService;
    private readonly GatewayService _gatewayService;
    private readonly AuthService _authService;
    private readonly VersionService _versionService;
    private GatewaySettings _settings = new();
    private string _statusMessage = string.Empty;

    public SettingsViewModel(
        GatewaySettingsService settingsService,
        GatewayService gatewayService,
        AuthService authService,
        VersionService versionService)
    {
        _settingsService = settingsService;
        _gatewayService = gatewayService;
        _authService = authService;
        _versionService = versionService;
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync());
    }

    public string OrganizationName
    {
        get => _settings.OrganizationName;
        set
        {
            _settings.OrganizationName = value;
            OnPropertyChanged();
        }
    }

    public string GatewayName
    {
        get => _settings.GatewayName;
        set
        {
            _settings.GatewayName = value;
            OnPropertyChanged();
        }
    }

    public bool IsAIGatewayEnabled
    {
        get => _settings.IsAIGatewayEnabled;
        set
        {
            _settings.IsAIGatewayEnabled = value;
            OnPropertyChanged();
        }
    }

    public string ReleaseVersion => _settings.ReleaseVersion;
    public string ReleaseDate => _settings.ReleaseDate;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand SaveCommand { get; }

    public async Task InitializeAsync()
    {
        var version = _versionService.GetVersionInfo();
        _settings = await _settingsService.LoadAsync() ?? new GatewaySettings
        {
            OrganizationId = _authService.OrganizationId,
            OrganizationName = "Organization",
            GatewayName = Environment.MachineName,
            ReleaseVersion = version.Version,
            ReleaseDate = version.ReleaseDate,
            IsAIGatewayEnabled = true
        };

        if (string.IsNullOrWhiteSpace(_settings.GatewayId))
        {
            _settings = await _gatewayService.RegisterGatewayAsync(_settings);
            await _settingsService.SaveAsync(_settings);
            StatusMessage = "Gateway registered successfully.";
        }

        OnPropertyChanged(nameof(OrganizationName));
        OnPropertyChanged(nameof(GatewayName));
        OnPropertyChanged(nameof(IsAIGatewayEnabled));
        OnPropertyChanged(nameof(ReleaseVersion));
        OnPropertyChanged(nameof(ReleaseDate));
    }

    private async Task SaveAsync()
    {
        _settings.OrganizationName = OrganizationName;
        _settings.GatewayName = GatewayName;
        _settings.IsAIGatewayEnabled = IsAIGatewayEnabled;
        await _settingsService.SaveAsync(_settings);
        await _gatewayService.SyncSettingsAsync(_settings);
        StatusMessage = "Settings saved.";
    }
}
