using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GatewayApp.Services;
using GatewayApp.ViewModels;

namespace GatewayApp.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;
    private bool _isPasswordVisible;
    private static readonly Random CaptchaRandom = new();

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
        if (e.PropertyName == nameof(LoginViewModel.CaptchaText))
        {
            RenderCaptcha(_vm.CaptchaText);
        }
    }

    /// <summary>
    /// Renders the CAPTCHA question directly onto <see cref="CaptchaCanvas"/> using WPF
    /// primitives. This avoids the unreliable <see cref="WebBrowser"/>/MSHTML host which
    /// does not render SVG data URIs by default.
    /// </summary>
    private void RenderCaptcha(string text)
    {
        CaptchaCanvas.Children.Clear();

        if (string.IsNullOrWhiteSpace(text))
        {
            var placeholder = new TextBlock
            {
                Text = "CAPTCHA unavailable.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x9D, 0xC3)),
                FontFamily = new FontFamily("Segoe UI")
            };
            Canvas.SetLeft(placeholder, 12);
            Canvas.SetTop(placeholder, 32);
            CaptchaCanvas.Children.Add(placeholder);
            return;
        }

        const double width = 360;
        const double height = 84;

        // Noise lines
        var noiseColors = new[]
        {
            Color.FromRgb(0xC4, 0xD3, 0xE0),
            Color.FromRgb(0xA8, 0xC0, 0xD4),
            Color.FromRgb(0xD0, 0xDC, 0xE6),
            Color.FromRgb(0xB5, 0xC9, 0xDB)
        };
        for (int i = 0; i < 7; i++)
        {
            var line = new Line
            {
                X1 = CaptchaRandom.NextDouble() * width,
                Y1 = CaptchaRandom.NextDouble() * height,
                X2 = CaptchaRandom.NextDouble() * width,
                Y2 = CaptchaRandom.NextDouble() * height,
                StrokeThickness = 1,
                Stroke = new SolidColorBrush(noiseColors[CaptchaRandom.Next(noiseColors.Length)])
            };
            CaptchaCanvas.Children.Add(line);
        }

        // Noise dots
        for (int i = 0; i < 30; i++)
        {
            var dot = new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = new SolidColorBrush(Color.FromArgb(0x80, 0xBC, 0xC8, 0xD4))
            };
            Canvas.SetLeft(dot, CaptchaRandom.NextDouble() * width);
            Canvas.SetTop(dot, CaptchaRandom.NextDouble() * height);
            CaptchaCanvas.Children.Add(dot);
        }

        // Characters with per-glyph rotation, offset, and color
        var charColors = new[]
        {
            Color.FromRgb(0x1E, 0x3A, 0x5F),
            Color.FromRgb(0x2C, 0x52, 0x82),
            Color.FromRgb(0x2B, 0x4C, 0x7E),
            Color.FromRgb(0x34, 0x49, 0x5E),
            Color.FromRgb(0x1A, 0x36, 0x5D)
        };

        double x = 18;
        foreach (var ch in text)
        {
            if (ch == ' ')
            {
                x += 10;
                continue;
            }

            var glyph = new TextBlock
            {
                Text = ch.ToString(),
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(charColors[CaptchaRandom.Next(charColors.Length)]),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(CaptchaRandom.Next(-15, 16))
            };

            var yOffset = 22 + CaptchaRandom.Next(-6, 7);
            Canvas.SetLeft(glyph, x);
            Canvas.SetTop(glyph, yOffset);
            CaptchaCanvas.Children.Add(glyph);

            x += 22;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
