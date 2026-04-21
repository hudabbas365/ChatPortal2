using GatewayApp.Recovery;

namespace GatewayApp.Services;

public static class AppServices
{
    private const string DefaultApiBaseUrl = "https://aiinsights365.net/";

    public static readonly FileEncryptionService Encryption = new();
    public static readonly VersionService Version = new();
    public static readonly AuthService Auth = new(DefaultApiBaseUrl);
    public static readonly GatewaySettingsService Settings = new(Encryption, DefaultApiBaseUrl);
    public static GatewaySettingsService GatewaySettings => Settings;
    public static readonly DiagnosticsService Diagnostics = new();
    public static readonly DatasourceService Datasource = new(DefaultApiBaseUrl, Auth);
    public static readonly GatewayService Gateway = new(DefaultApiBaseUrl, Auth, Diagnostics);
    public static readonly RecoveryService Recovery = new(Settings, Encryption, Diagnostics);
}
