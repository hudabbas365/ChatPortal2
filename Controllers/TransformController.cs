using AIInsights.Services.Transforms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIInsights.Controllers;

/// <summary>
/// Exposes metadata about the registered transform rule handlers so the
/// workbench UI can build its ribbon dynamically instead of hard-coding
/// the <c>RibbonGroups</c> constant in JavaScript.
/// </summary>
[Authorize]
[Route("api/transforms")]
[ApiController]
public class TransformController : ControllerBase
{
    private readonly IEnumerable<ITransformRuleHandler> _handlers;

    public TransformController(IEnumerable<ITransformRuleHandler> handlers)
    {
        _handlers = handlers;
    }

    /// <summary>
    /// Returns the list of all registered transform rule handlers.
    /// Each entry contains the rule <c>type</c> (TOML key) and its UI <c>group</c>
    /// so the workbench ribbon can be populated without hard-coded lists.
    /// </summary>
    [HttpGet("handlers")]
    public IActionResult GetHandlers() =>
        Ok(_handlers.Select(h => new { type = h.Type, group = h.Group }));
}
