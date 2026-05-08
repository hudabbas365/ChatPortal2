using AIInsights.Services;
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
    private readonly CohereService _cohere;

    public TransformController(IEnumerable<ITransformRuleHandler> handlers, CohereService cohere)
    {
        _handlers = handlers;
        _cohere = cohere;
    }

    /// <summary>
    /// Returns the list of all registered transform rule handlers.
    /// Each entry contains the rule <c>type</c> (TOML key) and its UI <c>group</c>
    /// so the workbench ribbon can be populated without hard-coded lists.
    /// </summary>
    [HttpGet("handlers")]
    public IActionResult GetHandlers() =>
        Ok(_handlers.Select(h => new { type = h.Type, group = h.Group }));

    public sealed class AiSuggestRequest
    {
        public string? DatasourceGuid { get; set; }
        public string? Op { get; set; }
        public List<string>? Columns { get; set; }
        public List<string>? OtherQueries { get; set; }
        public string? Prompt { get; set; }
    }

    /// <summary>
    /// Translates a free-form prompt (from the workbench AI assistant pane or the
    /// in-form AI assist strip) into a JSON document of transform steps using the
    /// Cohere LLM. Falls back to <c>{steps:[]}</c> when the prompt is empty,
    /// the API key is missing, or the model response cannot be parsed - the
    /// client then degrades to its local intent parser.
    /// </summary>
    [HttpPost("ai-suggest")]
    [Consumes("application/json")]
    public async Task<IActionResult> AiSuggest([FromBody] AiSuggestRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Prompt))
            return Ok(new { steps = System.Array.Empty<object>() });

        var raw = await _cohere.SuggestTransformStepsAsync(
            req.Prompt!, req.Columns, req.OtherQueries, req.Op);

        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(raw);
            return Content(raw, "application/json");
        }
        catch
        {
            return Ok(new { steps = System.Array.Empty<object>() });
        }
    }
}
