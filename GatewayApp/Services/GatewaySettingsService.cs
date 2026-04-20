using System.Text.Json;
using System.IO;
using GatewayApp.Models;

namespace GatewayApp.Services;

public sealed class GatewaySettingsService
{
    private readonly FileEncryptionService _encryptionService;
    private readonly string _configPath;

    public GatewaySettingsService(FileEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIInsights365Gateway");
        Directory.CreateDirectory(dataDir);
        _configPath = Path.Combine(dataDir, "gateway-config.json");
    }

    public async Task<GatewaySettings?> LoadAsync()
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }

        var encrypted = await File.ReadAllTextAsync(_configPath).ConfigureAwait(false);
        var json = _encryptionService.Decrypt(encrypted);
        return JsonSerializer.Deserialize<GatewaySettings>(json, JsonDefaults.Options);
    }

    public async Task SaveAsync(GatewaySettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var encrypted = _encryptionService.Encrypt(json);
        await File.WriteAllTextAsync(_configPath, encrypted).ConfigureAwait(false);
    }

    public string GetConfigPath() => _configPath;
}
