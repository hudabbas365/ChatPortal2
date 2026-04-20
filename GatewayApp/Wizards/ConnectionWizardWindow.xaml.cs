using System.Windows;
using System.Windows.Controls;
using GatewayApp.ViewModels;

namespace GatewayApp.Wizards;

public partial class ConnectionWizardWindow : Window
{
    public ConnectionWizardWindow()
    {
        InitializeComponent();
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConnectionWizardViewModel vm && sender is PasswordBox password)
        {
            vm.Password = password.Password;
        }
    }
}
