using System.Text.Json;
using System.IO;
using GatewayApp.Models;
using GatewayApp.Services;

namespace GatewayApp.Recovery;

public sealed class RecoveryService
{
    private readonly GatewaySettingsService _settingsService;
    private readonly FileEncryptionService _encryptionService;
    private readonly DiagnosticsService _diagnosticsService;

    public RecoveryService(
        GatewaySettingsService settingsService,
        FileEncryptionService encryptionService,
        DiagnosticsService diagnosticsService)
    {
        _settingsService = settingsService;
        _encryptionService = encryptionService;
        _diagnosticsService = diagnosticsService;
    }

    public async Task BackupConfigurationAsync()
    {
        var settings = await _settingsService.LoadAsync().ConfigureAwait(false);
        var backup = new
        {
            Settings = settings,
            BackupUtc = DateTime.UtcNow,
            ConfigPath = _settingsService.GetConfigPath()
        };

        var dataDir = Path.GetDirectoryName(_settingsService.GetConfigPath())
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var backupPath = Path.Combine(dataDir, $"gateway-backup-{DateTime.UtcNow:yyyyMMddHHmmss}.bak");

        var json = JsonSerializer.Serialize(backup, JsonDefaults.Options);
        var encrypted = _encryptionService.Encrypt(json);
        await File.WriteAllTextAsync(backupPath, encrypted).ConfigureAwait(false);
    }

    public async Task<bool> RestoreConfigurationAsync(string backupFilePath)
    {
        if (!File.Exists(backupFilePath))
        {
            return false;
        }

        var encrypted = await File.ReadAllTextAsync(backupFilePath).ConfigureAwait(false);
        var json = _encryptionService.Decrypt(encrypted);

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("settings", out var settingsElement))
        {
            return false;
        }

        var settings = settingsElement.Deserialize<GatewaySettings>(JsonDefaults.Options);
        if (settings is null)
        {
            return false;
        }

        await _settingsService.SaveAsync(settings).ConfigureAwait(false);
        return true;
    }

    public async Task HandleFailoverAsync(string datasourceId)
    {
        var delays = new[] { 250, 500, 1000 };
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            _diagnosticsService.LogTransaction(new TransactionLog
            {
                DatasourceId = datasourceId,
                DatasourceName = datasourceId,
                Query = "Failover reconnect",
                Status = "Failed",
                ErrorMessage = $"Reconnect attempt {attempt + 1}"
            });

            await Task.Delay(delays[attempt]).ConfigureAwait(false);
        }
    }
}
