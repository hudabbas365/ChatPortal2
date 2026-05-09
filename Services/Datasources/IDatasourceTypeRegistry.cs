using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AIInsights.Services.Datasources;

/// <summary>
/// Read-side facade over <see cref="DatasourceTypeDefinition"/> rows. Drives
/// <c>GET /api/datasources/types</c> and <c>GET /api/datasources/type-schemas</c>
/// so adding/changing a connection field is a single edit in the SuperAdmin
/// admin UI — no redeploy required.
/// </summary>
public interface IDatasourceTypeRegistry
{
    Task<IReadOnlyList<DatasourceTypeSchema>> GetSchemasAsync(bool includeDisabled = false, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetTypeNamesAsync(bool includeDisabled = false, CancellationToken ct = default);
    Task<DatasourceTypeSchema?> FindByTypeAsync(string type, CancellationToken ct = default);
}

/// <summary>Wire-shape consumed by the JS clients (workspaceWizard + Transform Workbench).</summary>
public sealed record DatasourceTypeSchema(
    string Type,
    string DisplayName,
    string? Description,
    string? Icon,
    bool Enabled,
    bool RequiresGateway,
    string? GatewayHelp,
    int SortOrder,
    bool IsBuiltIn,
    IReadOnlyList<DatasourceTypeSchemaParameter> Parameters);

public sealed record DatasourceTypeSchemaParameter(
    string Key,
    string Label,
    string Type,
    string? Placeholder,
    bool Required,
    IReadOnlyList<string>? Options,
    string? Help);

public class DatasourceTypeRegistry : IDatasourceTypeRegistry
{
    private readonly AppDbContext _db;

    public DatasourceTypeRegistry(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<DatasourceTypeSchema>> GetSchemasAsync(bool includeDisabled = false, CancellationToken ct = default)
    {
        var q = _db.DatasourceTypeDefinitions
            .AsNoTracking()
            .Include(d => d.Parameters)
            .AsQueryable();

        if (!includeDisabled) q = q.Where(d => d.Enabled);

        var rows = await q.OrderBy(d => d.SortOrder).ThenBy(d => d.DisplayName).ToListAsync(ct);
        return rows.Select(ToSchema).ToList();
    }

    public async Task<IReadOnlyList<string>> GetTypeNamesAsync(bool includeDisabled = false, CancellationToken ct = default)
    {
        var q = _db.DatasourceTypeDefinitions.AsNoTracking().AsQueryable();
        if (!includeDisabled) q = q.Where(d => d.Enabled);
        return await q.OrderBy(d => d.SortOrder).ThenBy(d => d.DisplayName)
            .Select(d => d.Type).ToListAsync(ct);
    }

    public async Task<DatasourceTypeSchema?> FindByTypeAsync(string type, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(type)) return null;
        var row = await _db.DatasourceTypeDefinitions
            .AsNoTracking()
            .Include(d => d.Parameters)
            .FirstOrDefaultAsync(d => d.Type == type, ct);
        return row == null ? null : ToSchema(row);
    }

    private static DatasourceTypeSchema ToSchema(DatasourceTypeDefinition d) => new(
        Type: d.Type,
        DisplayName: d.DisplayName,
        Description: d.Description,
        Icon: d.Icon,
        Enabled: d.Enabled,
        RequiresGateway: d.RequiresGateway,
        GatewayHelp: d.GatewayHelp,
        SortOrder: d.SortOrder,
        IsBuiltIn: d.IsBuiltIn,
        Parameters: d.Parameters
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .Select(p => new DatasourceTypeSchemaParameter(
                Key: p.Key,
                Label: p.Label,
                Type: p.Type,
                Placeholder: p.Placeholder,
                Required: p.Required,
                Options: ParseOptions(p.OptionsJson),
                Help: p.Help))
            .ToList());

    public static IReadOnlyList<string>? ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(json);
            return arr;
        }
        catch { return null; }
    }
}
