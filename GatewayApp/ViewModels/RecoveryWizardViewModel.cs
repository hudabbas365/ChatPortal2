using System.Windows.Input;
using Microsoft.Win32;
using GatewayApp.Recovery;

namespace GatewayApp.ViewModels;

public sealed class RecoveryWizardViewModel : ViewModelBase
{
    private readonly RecoveryService _recoveryService;
    private int _stepIndex;
    private bool _isBackupSelected = true;
    private string _backupFilePath = string.Empty;
    private string _resultMessage = string.Empty;

    public RecoveryWizardViewModel(RecoveryService recoveryService)
    {
        _recoveryService = recoveryService;
        NextCommand = new AsyncRelayCommand(_ => NextAsync());
        BackCommand = new RelayCommand(_ => StepIndex = Math.Max(0, StepIndex - 1));
        BrowseCommand = new RelayCommand(_ => BrowseBackup());
    }

    public int StepIndex
    {
        get => _stepIndex;
        set => SetProperty(ref _stepIndex, value);
    }

    public bool IsBackupSelected
    {
        get => _isBackupSelected;
        set => SetProperty(ref _isBackupSelected, value);
    }

    public string BackupFilePath
    {
        get => _backupFilePath;
        set => SetProperty(ref _backupFilePath, value);
    }

    public string ResultMessage
    {
        get => _resultMessage;
        set => SetProperty(ref _resultMessage, value);
    }

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand BrowseCommand { get; }

    private async Task NextAsync()
    {
        if (StepIndex == 0)
        {
            StepIndex = 1;
            return;
        }

        if (StepIndex == 1)
        {
            if (IsBackupSelected)
            {
                await _recoveryService.BackupConfigurationAsync();
                ResultMessage = "Backup completed.";
            }
            else
            {
                var restored = await _recoveryService.RestoreConfigurationAsync(BackupFilePath);
                ResultMessage = restored ? "Restore completed." : "Restore failed.";
            }

            StepIndex = 2;
            return;
        }

        Close();
    }

    private void BrowseBackup()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Backup Files (*.bak)|*.bak"
        };

        if (dialog.ShowDialog() == true)
        {
            BackupFilePath = dialog.FileName;
        }
    }

    private static void Close()
    {
        var window = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().SingleOrDefault(w => w.IsActive);
        window?.Close();
    }
}
