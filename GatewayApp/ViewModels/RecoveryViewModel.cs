using System.Windows;
using System.Windows.Input;
using GatewayApp.Recovery;

namespace GatewayApp.ViewModels;

public sealed class RecoveryViewModel : ViewModelBase
{
    private readonly RecoveryService _recoveryService;

    public RecoveryViewModel(RecoveryService recoveryService)
    {
        _recoveryService = recoveryService;
        OpenRecoveryWizardCommand = new RelayCommand(_ => OpenWizard());
    }

    public ICommand OpenRecoveryWizardCommand { get; }

    private void OpenWizard()
    {
        var vm = new RecoveryWizardViewModel(_recoveryService);
        var wizard = new Recovery.RecoveryWizardWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        wizard.ShowDialog();
    }
}
