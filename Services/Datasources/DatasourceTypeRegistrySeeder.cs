using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AIInsights.Services.Datasources;

/// <summary>
/// Idempotent one-shot import of the built-in datasource type catalog from
/// the registered <see cref="IDatasourceTypeService"/> implementations into
/// the <see cref="DatasourceTypeDefinition"/> / <see cref="DatasourceTypeParameter"/>
/// tables. Runs once on app startup; subsequent runs only insert NEW types
/// or NEW parameters — admin edits to existing rows are preserved.
/// </summary>
public interface IDatasourceTypeRegistrySeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public class DatasourceTypeRegistrySeeder : IDatasourceTypeRegistrySeeder
{
    private readonly AppDbContext _db;
    private readonly IEnumerable<IDatasourceTypeService> _services;
    private readonly ILogger<DatasourceTypeRegistrySeeder> _logger;

    public DatasourceTypeRegistrySeeder(
        AppDbContext db,
        IEnumerable<IDatasourceTypeService> services,
        ILogger<DatasourceTypeRegistrySeeder> logger)
    {
        _db = db;
        _services = services;
        _logger = logger;
    }

    private static readonly Dictionary<string, (string Display, string Icon, bool RequiresGateway, string? GatewayHelp, int SortOrder, string? Description)> Meta =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SQL Server"] = ("SQL Server", "bi-database", true,
                "Connect to on-prem SQL Server via the AIInsights Gateway. Cloud-hosted SQL (Azure SQL, RDS) does not require the gateway.",
                10, "Microsoft SQL Server (on-prem or cloud) via ADO.NET connection string."),
            ["Power BI"] = ("Power BI", "bi-bar-chart-line", false, null,
                20, "Microsoft Power BI dataset accessed via XMLA endpoint."),
            ["REST API"] = ("REST API", "bi-globe", false, null,
                30, "Any HTTP(S) JSON endpoint. Schema is inferred from the first sample response."),
            ["File URL"] = ("File URL", "bi-file-earmark-spreadsheet", false, null,
                40, "Public CSV or XLSX file downloaded and parsed on each refresh."),
        };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var existing = await _db.DatasourceTypeDefinitions
            .Include(d => d.Parameters)
            .ToListAsync(ct);

        var byType = existing.ToDictionary(d => d.Type, StringComparer.OrdinalIgnoreCase);
        var addedDefs = 0;
        var addedParams = 0;

        foreach (var svc in _services)
        {
            foreach (var typeName in svc.SupportedTypeStrings)
            {
                if (!byType.TryGetValue(typeName, out var def))
                {
                    var meta = Meta.TryGetValue(typeName, out var m)
                        ? m
                        : (Display: typeName, Icon: "bi-database", RequiresGateway: false,
                           GatewayHelp: (string?)null, SortOrder: 100, Description: (string?)null);

                    def = new DatasourceTypeDefinition
                    {
                        Type = typeName,
                        DisplayName = meta.Display,
                        Description = meta.Description,
                        Icon = meta.Icon,
                        Enabled = true,
                        SortOrder = meta.SortOrder,
                        RequiresGateway = meta.RequiresGateway,
                        GatewayHelp = meta.GatewayHelp,
                        IsBuiltIn = true,
                        CreatedUtc = DateTime.UtcNow,
                        UpdatedUtc = DateTime.UtcNow,
                        UpdatedBy = "system:seeder"
                    };
                    _db.DatasourceTypeDefinitions.Add(def);
                    byType[typeName] = def;
                    addedDefs++;
                }
                else if (!def.IsBuiltIn)
                {
                    // Mark as built-in if a service later claims it (so the
                    // admin UI's delete guard kicks in retroactively).
                    def.IsBuiltIn = true;
                }

                // Reconcile params — add only NEW Keys; preserve admin edits.
                var existingKeys = new HashSet<string>(def.Parameters.Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
                var sortOrder = def.Parameters.Count == 0 ? 0
                              : def.Parameters.Max(p => p.SortOrder) + 10;

                foreach (var p in svc.Parameters)
                {
                    if (existingKeys.Contains(p.Key)) continue;

                    var optionsJson = p.Options != null && p.Options.Count > 0
                        ? JsonSerializer.Serialize(p.Options)
                        : null;

                    def.Parameters.Add(new DatasourceTypeParameter
                    {
                        Key = p.Key,
                        Label = p.Label,
                        Type = p.Type ?? "text",
                        Placeholder = p.Placeholder,
                        Required = p.Required,
                        OptionsJson = optionsJson,
                        Help = p.Help,
                        SortOrder = sortOrder
                    });
                    sortOrder += 10;
                    addedParams++;
                }
            }
        }

        if (addedDefs > 0 || addedParams > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "DatasourceTypeRegistrySeeder: added {Defs} type(s), {Params} parameter(s).",
                addedDefs, addedParams);
        }
        else
        {
            _logger.LogInformation("DatasourceTypeRegistrySeeder: registry already up to date.");
        }
    }
}
