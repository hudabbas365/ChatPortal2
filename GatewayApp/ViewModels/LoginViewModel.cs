using System.Windows.Input;
using GatewayApp.Models;
using GatewayApp.Services;

namespace GatewayApp.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly GatewaySettingsService _settingsService;
    private readonly VersionService _versionService;

    private string _email = string.Empty;
    private string _errorMessage = string.Empty;
    private string _captchaAnswer = string.Empty;
    private bool _isLoading;
    private string _captchaId = string.Empty;
    private string _captchaImageDataUri = string.Empty;

    public LoginViewModel(AuthService authService, GatewaySettingsService settingsService, VersionService versionService)
    {
        _authService = authService;
        _settingsService = settingsService;
        _versionService = versionService;

        LoginCommand = new RelayCommand<string?>(ExecuteLoginAsync, _ => !IsLoading);
        RefreshCaptchaCommand = new RelayCommand(_ => LoadCaptchaAsync(), _ => !IsLoading);
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string CaptchaAnswer
    {
        get => _captchaAnswer;
        set => SetProperty(ref _captchaAnswer, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string CaptchaImageDataUri
    {
        get => _captchaImageDataUri;
        set => SetProperty(ref _captchaImageDataUri, value);
    }

    public string AppVersion => $"v{_versionService.Version}";

    public ICommand LoginCommand { get; }
    public ICommand RefreshCaptchaCommand { get; }

    public async Task InitializeAsync()
    {
        await LoadCaptchaAsync();
    }

    public event EventHandler<AuthResult>? LoginSucceeded;

    private async Task LoadCaptchaAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var challenge = await _authService.GetCaptchaAsync();
            _captchaId = challenge.CaptchaId;
            CaptchaImageDataUri = challenge.Image;
            CaptchaAnswer = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load CAPTCHA: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExecuteLoginAsync(string? password)
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(password))
        {
            ErrorMessage = "Email and password are required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_captchaId) || string.IsNullOrWhiteSpace(CaptchaAnswer))
        {
            ErrorMessage = "Please solve the CAPTCHA before logging in.";
            return;
        }

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var result = await _authService.LoginAsync(Email.Trim(), password, _captchaId, CaptchaAnswer.Trim());
            await TryRegisterGatewayAsync(result);

            LoginSucceeded?.Invoke(this, result);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            await LoadCaptchaAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
            await LoadCaptchaAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task TryRegisterGatewayAsync(AuthResult authResult)
    {
        try
        {
            var settings = await _settingsService.LoadAsync() ?? new GatewaySettings
            {
                GatewayName = Environment.MachineName
            };

            await _settingsService.RegisterGatewayAsync(
                authResult.OrganizationId,
                settings.GatewayName,
                _versionService.Version,
                _versionService.ReleaseDate,
                authResult.Token);
        }
        catch
        {
            // Best effort: non-fatal by design.
        }
    }

    private void RaiseCommandCanExecuteChanged()
    {
        if (LoginCommand is RelayCommand<string?> login)
        {
            login.RaiseCanExecuteChanged();
        }

        if (RefreshCaptchaCommand is RelayCommand refresh)
        {
            refresh.RaiseCanExecuteChanged();
        }
    }
}
