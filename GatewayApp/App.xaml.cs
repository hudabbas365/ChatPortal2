using System.Windows;
using GatewayApp.Services;
using GatewayApp.ViewModels;
using GatewayApp.Views;

namespace GatewayApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var loginWindow = new LoginWindow();
        loginWindow.DataContext = new LoginViewModel(AppServices.Auth, () =>
        {
            var mainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };

            MainWindow = mainWindow;
            mainWindow.Show();
            loginWindow.Close();
        });

        MainWindow = loginWindow;
        loginWindow.Show();
    }
}
