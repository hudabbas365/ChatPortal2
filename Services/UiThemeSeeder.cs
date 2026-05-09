using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.Services;

public interface IUiThemeSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public class UiThemeSeeder : IUiThemeSeeder
{
    private readonly AppDbContext _db;
    private readonly ILogger<UiThemeSeeder> _logger;

    public UiThemeSeeder(AppDbContext db, ILogger<UiThemeSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Canonical defaults captured from wwwroot/css/site.css :root block.
    // Idempotent: only NEW keys are inserted; existing rows (including admin edits) are preserved.
    private static readonly (string Key, string Value, string Kind, string Category, int Sort, string? Description)[] Defaults =
    {
        // Brand
        ("cp-primary",       "#5B9BD5", "color",    "Brand",   10, "Primary brand color."),
        ("cp-primary-dark",  "#3A7BBF", "color",    "Brand",   11, "Darker shade of primary."),
        ("cp-primary-light", "#EBF4FF", "color",    "Brand",   12, "Light tint of primary."),
        ("cp-accent",        "#7EC8A4", "color",    "Brand",   13, "Accent color."),
        ("cp-info",          "#74B9E0", "color",    "Brand",   14, "Info color."),
        ("cp-gradient",      "linear-gradient(135deg, #5B9BD5 0%, #3A7BBF 100%)", "gradient", "Brand", 15, "Primary gradient."),
        ("cp-gradient-bg",   "linear-gradient(160deg, #EBF4FF 0%, #F4F7FB 60%, #DCEEFB 100%)", "gradient", "Brand", 16, "Background gradient."),

        // Surface
        ("cp-bg",            "#F4F7FB", "color",  "Surface", 20, "App background."),
        ("cp-surface",       "#FFFFFF", "color",  "Surface", 21, "Surface / card background."),
        ("cp-border",        "#DDE8F0", "color",  "Surface", 22, "Default border color."),
        ("cp-shadow",        "0 4px 20px rgba(91, 155, 213, 0.12)", "shadow", "Surface", 23, "Elevated shadow."),
        ("cp-shadow-sm",     "0 1px 4px rgba(0, 0, 0, 0.08)",       "shadow", "Surface", 24, "Subtle shadow."),
        ("cp-radius",        "10px",    "radius", "Surface", 25, "Default corner radius."),

        // Text
        ("cp-text",          "#1E2D3D", "color", "Text", 30, "Primary text color."),
        ("cp-text-muted",    "#7A90A8", "color", "Text", 31, "Muted text color."),
        ("cp-transition",    "all 0.2s ease", "other", "Text", 32, "Default transition."),

        // Status
        ("cp-danger",        "#dc3545", "color", "Status", 40, "Danger / error color."),
        ("cp-danger-light",  "#fff0f0", "color", "Status", 41, "Light danger background."),
        ("cp-success",       "#2ECC71", "color", "Status", 42, "Success color."),
        ("cp-success-light", "#EAFAF1", "color", "Status", 43, "Light success background."),
        ("cp-purple",        "#9B59B6", "color", "Status", 44, "Purple accent."),
        ("cp-purple-light",  "#F3E8FF", "color", "Status", 45, "Light purple background."),
        ("cp-orange",        "#E67E22", "color", "Status", 46, "Orange / warning color."),
        ("cp-orange-light",  "#FFF3E0", "color", "Status", 47, "Light orange background."),

        // Code
        ("cp-code-bg",       "#1E2D3D", "color", "Code", 50, "Code block background."),
        ("cp-code-text",     "#A8C4DF", "color", "Code", 51, "Code block text."),
        ("cp-code-border",   "#2A3F55", "color", "Code", 52, "Code block border."),
        ("cp-font-mono",     "'Fira Code', 'Consolas', monospace", "other", "Code", 53, "Monospace font stack."),

        // Layout
        ("navbar-h",         "60px",  "size", "Layout", 60, "Top navbar height."),
        ("library-w",        "260px", "size", "Layout", 61, "Library panel width."),
        ("properties-w",     "300px", "size", "Layout", 62, "Properties panel width."),
        ("right-panels-w",   "540px", "size", "Layout", 63, "Right panels combined width."),
    };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var existing = await _db.UiThemeVariables
            .Select(v => v.Key)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var nowUtc = DateTime.UtcNow;
        foreach (var (key, value, kind, category, sort, description) in Defaults)
        {
            if (existingSet.Contains(key)) continue;

            _db.UiThemeVariables.Add(new UiThemeVariable
            {
                Key = key,
                Value = value,
                DefaultValue = value,
                Kind = kind,
                Category = category,
                SortOrder = sort,
                Description = description,
                UpdatedUtc = nowUtc,
                UpdatedBy = "system"
            });
            added++;
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("UiThemeSeeder: inserted {Count} new theme variables.", added);
        }
    }
}
