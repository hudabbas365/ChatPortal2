using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Input;
using GatewayApp.Services;
using GatewayApp.ViewModels;

namespace GatewayApp.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;
    private bool _isPasswordVisible;

    public LoginWindow()
    {
        InitializeComponent();

        _vm = new LoginViewModel(
            AppServices.Auth,
            AppServices.GatewaySettings,
            AppServices.Version);

        _vm.LoginSucceeded += (_, _) =>
        {
            var main = new MainWindow();
            main.Show();
            Close();
        };

        _vm.PropertyChanged += ViewModelOnPropertyChanged;
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.InitializeAsync();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var password = _isPasswordVisible ? PasswordRevealInput.Text : PasswordInput.Password;

        if (_vm.LoginCommand.CanExecute(password))
        {
            _vm.LoginCommand.Execute(password);
        }
    }

    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;

        if (_isPasswordVisible)
        {
            PasswordRevealInput.Text = PasswordInput.Password;
            PasswordInput.Visibility = Visibility.Collapsed;
            PasswordRevealInput.Visibility = Visibility.Visible;
            EyeIcon.Text = "\uE890";
            PasswordRevealInput.Focus();
            PasswordRevealInput.CaretIndex = PasswordRevealInput.Text.Length;
            return;
        }

        PasswordInput.Password = PasswordRevealInput.Text;
        PasswordRevealInput.Visibility = Visibility.Collapsed;
        PasswordInput.Visibility = Visibility.Visible;
        EyeIcon.Text = "\uE722";
        PasswordInput.Focus();
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isPasswordVisible)
        {
            PasswordRevealInput.Text = PasswordInput.Password;
            PasswordRevealInput.CaretIndex = PasswordRevealInput.Text.Length;
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.CaptchaImageDataUri))
        {
            RenderCaptcha(_vm.CaptchaImageDataUri);
        }
    }

    private void RenderCaptcha(string dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri) ||
            !dataUri.StartsWith("data:image/svg+xml;base64,", StringComparison.OrdinalIgnoreCase))
        {
            CaptchaBrowser.NavigateToString("<html><body style='background:#0F172A;color:#8B9DC3;font-family:Segoe UI;'>CAPTCHA unavailable.</body></html>");
            return;
        }

        var safeDataUri = WebUtility.HtmlEncode(dataUri);
        var html = $"<html><body style='margin:0;padding:0;background:#0F172A;display:flex;align-items:center;justify-content:center;'><img alt='captcha' style='max-height:84px;max-width:100%;' src='{safeDataUri}'/></body></html>";
        CaptchaBrowser.NavigateToString(html);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
