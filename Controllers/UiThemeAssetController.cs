using AIInsights.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIInsights.Controllers;

/// <summary>
/// Public endpoint serving the DB-backed CSS theme as a regular stylesheet.
/// Loaded after <c>site.css</c> in the main <c>_Layout.cshtml</c> so admin
/// edits override the canonical defaults without touching the file system.
/// </summary>
[ApiController]
public class UiThemeAssetController : ControllerBase
{
    private readonly IUiThemeService _theme;

    public UiThemeAssetController(IUiThemeService theme) { _theme = theme; }

    [HttpGet("/css/theme.css")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Theme(CancellationToken ct)
    {
        var css = await _theme.GetThemeCssAsync(ct);
        return Content(css, "text/css");
    }
}
