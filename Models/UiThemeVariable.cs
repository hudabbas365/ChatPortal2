using System.ComponentModel.DataAnnotations;

namespace AIInsights.Models;

/// <summary>
/// Single CSS custom-property (`--cp-primary`, `--cp-success`, …) stored in the
/// DB so SuperAdmin can rebrand the entire UI from the admin panel without
/// editing <c>wwwroot/css/site.css</c>. Every variable here is emitted into
/// <c>:root { … }</c> by <see cref="AIInsights.Services.IUiThemeService"/>
/// and served from <c>GET /css/theme.css</c>.
/// </summary>
public class UiThemeVariable
{
    public int Id { get; set; }

    /// <summary>
    /// CSS variable name without the leading "--", e.g. <c>cp-primary</c>.
    /// Stored without the dashes so we can reuse the value as a Razor field id.
    /// </summary>
    [Required, MaxLength(64)]
    public string Key { get; set; } = "";

    [Required, MaxLength(512)]
    public string Value { get; set; } = "";

    /// <summary>Default value seeded from <c>site.css</c>. Drives the Reset button.</summary>
    [MaxLength(512)]
    public string? DefaultValue { get; set; }

    /// <summary>One of: <c>color | gradient | shadow | radius | size | other</c>.</summary>
    [Required, MaxLength(24)]
    public string Kind { get; set; } = "color";

    /// <summary>Group label rendered as a section header in the admin form.</summary>
    [MaxLength(64)]
    public string? Category { get; set; }

    [MaxLength(256)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UpdatedBy { get; set; }
}
