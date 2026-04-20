using System.Text.Json;
using System.IO;
using GatewayApp.Models;

namespace GatewayApp.Services;

public sealed class VersionService
{
    public VersionInfo GetVersionInfo()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "version.json");
        if (!File.Exists(filePath))
        {
            return new VersionInfo();
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<VersionInfo>(json, JsonDefaults.Options) ?? new VersionInfo();
    }
}
