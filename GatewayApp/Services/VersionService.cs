using System.IO;
using System.Text.Json;
using GatewayApp.Models;

namespace GatewayApp.Services;

public sealed class VersionService
{
    public string Version => GetVersionInfo().Version;
    public string ReleaseDate => GetVersionInfo().ReleaseDate;

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
