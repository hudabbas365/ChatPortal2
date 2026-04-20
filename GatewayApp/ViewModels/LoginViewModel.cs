using System.Windows.Controls;
using System.Windows.Input;
using GatewayApp.Services;

namespace GatewayApp.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly AuthService _authService;
    private readonly Action _onLoginSuccess;
    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public LoginViewModel(AuthService authService, Action onLoginSuccess)
    {
        _authService = authService;
        _onLoginSuccess = onLoginSuccess;
        LoginCommand = new AsyncRelayCommand(LoginAsync, _ => !IsBusy && !string.IsNullOrWhiteSpace(Username));
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value) && LoginCommand is AsyncRelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand LoginCommand { get; }

    private async Task LoginAsync(object? parameter)
    {
        if (parameter is not PasswordBox passwordBox)
        {
            ErrorMessage = "Password is required.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            await _authService.LoginAsync(Username.Trim(), passwordBox.Password);
            _onLoginSuccess();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
