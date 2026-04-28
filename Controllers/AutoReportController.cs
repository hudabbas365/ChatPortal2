using AIInsights.Data;
using AIInsights.Filters;
using AIInsights.Models;
using AIInsights.Services;
using AIInsights.Services.AutoReport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;

namespace AIInsights.Controllers;

/// <summary>
/// Thin dispatcher for the AI Auto-Report generator. The heavy lifting
/// (per-datasource prompt rules + schema introspection) lives in the
/// <see cref="IAutoReportBuilder"/> implementations under Services/AutoReport,
/// one file per datasource type (SQL Server, Power BI, REST API, File URL).
/// </summary>
[Authorize]
[Route("api/auto-report")]
[ApiController]
public class AutoReportController : ControllerBase
{
    private readonly CohereService _cohere;
    private readonly AppDbContext _db;
    private readonly IRelationshipService _relationships;
    private readonly IEnumerable<IAutoReportBuilder> _builders;
    private readonly ITokenBudgetService _tokenBudget;

    public AutoReportController(
        CohereService cohere,
        AppDbContext db,
        IRelationshipService relationships,
        IEnumerable<IAutoReportBuilder> builders,
        ITokenBudgetService tokenBudget)
    {
        _cohere = cohere;
        _db = db;
        _relationships = relationships;
        _builders = builders;
        _tokenBudget = tokenBudget;
    }

    public class AutoReportRequest
    {
        public string? WorkspaceId { get; set; }
        public string? UserId { get; set; }
        public string? DatasourceId { get; set; }
        public string? Prompt { get; set; }
        public List<string> TableNames { get; set; } = new();
        public string? ExistingCharts { get; set; }
    }

    [HttpPost("generate")]
    [RequireActiveSubscription]
    public async Task Generate([FromBody] AutoReportRequest req)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        // ── Token usage accounting (mirrors ChatController) ──────
        // Auto-report streaming bypassed TokenBudgetService entirely, so the
        // org-admin "AI Token Usage (This Month)" tile never reflected report
        // generations. We track input + output character totals here and
        // record once on completion (success OR error) using the same
        // ~4-chars-per-token estimate the chat path uses.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";
        int? orgId = null;
        if (!string.IsNullOrEmpty(userId))
        {
            orgId = await _db.Users.Where(u => u.Id == userId).Select(u => u.OrganizationId).FirstOrDefaultAsync();
        }
        var inputChars = 0;
        var outputChars = 0;

        try
        {
            // ── Pre-flight token budget check (H4) ───────────────
            // Mirror ChatController: if the org has exhausted its monthly AI
            // token budget we must refuse BEFORE doing any LLM work, otherwise
            // the org continues consuming Cohere quota with no accounting cap.
            if (orgId.HasValue && orgId.Value > 0 && !await _tokenBudget.HasBudgetAsync(orgId.Value))
            {
                await Response.WriteAsync("data: {\"error\":\"Monthly AI token budget exceeded. Please upgrade your plan or buy a token pack to continue.\",\"code\":\"budget_exceeded\"}\n\n");
                await Response.WriteAsync("data: [DONE]\n\n");
                await Response.Body.FlushAsync();
                return;
            }

            // ── Plan gate ────────────────────────────────────────────
            // AI Auto-Report is an Enterprise (and Free Trial) feature.
            // Professional is chat + dashboards only — explicitly blocked here.
            if (!string.IsNullOrEmpty(req.UserId))
            {
                var userSub = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == req.UserId);
                PlanType effectivePlan;
                if (userSub != null)
                {
                    effectivePlan = userSub.Plan;
                }
                else
                {
                    var user = await _db.Users.FindAsync(req.UserId);
                    var org = user?.OrganizationId != null ? await _db.Organizations.FindAsync(user.OrganizationId) : null;
                    effectivePlan = org?.Plan ?? PlanType.Free;
                }
                if (!PlanFeatures.AllowsAutoReport(effectivePlan))
                {
                    await Response.WriteAsync("data: {\"error\":\"AI Auto-Report generation is not available on your current plan. Upgrade to Enterprise to unlock this feature.\",\"code\":\"plan_gated\"}\n\n");
                    await Response.WriteAsync("data: [DONE]\n\n");
                    await Response.Body.FlushAsync();
                    return;
                }
            }

            // ── Resolve datasource ───────────────────────────────────
            Datasource? ds = null;
            if (!string.IsNullOrEmpty(req.DatasourceId) && int.TryParse(req.DatasourceId, out var dsId))
            {
                ds = await _db.Datasources.FindAsync(dsId);
            }
            if (ds == null && !string.IsNullOrEmpty(req.WorkspaceId))
            {
                var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == req.WorkspaceId);
                if (ws != null)
                    ds = await _db.Datasources.FirstOrDefaultAsync(d => d.WorkspaceId == ws.Id);
            }

            // ── Pick the per-datasource builder (SQL fallback) ───────
            var builder = _builders.FirstOrDefault(b => b.CanHandle(ds?.Type))
                          ?? _builders.OfType<SqlAutoReportBuilder>().FirstOrDefault()
                          ?? throw new InvalidOperationException("No auto-report builder registered.");

            // ── Build schema snippet ─────────────────────────────────
            var schemaSnippet = "";
            if (ds != null)
            {
                try { schemaSnippet = await builder.BuildSchemaSnippetAsync(ds); }
                catch { schemaSnippet = "Schema not available."; }
            }

            var tables = req.TableNames.Count > 0
                ? string.Join(", ", req.TableNames)
                : "No specific tables provided";

            // Discover table relationships so the AI emits FK-correct JOINs.
            var relationshipsSnippet = "";
            if (ds != null)
            {
                try
                {
                    var rels = await _relationships.GetRelationshipsAsync(ds);
                    relationshipsSnippet = BuildRelationshipsSnippet(rels);
                }
                catch { /* best-effort */ }
            }

            var systemPrompt = builder.BuildSystemPrompt(tables, schemaSnippet, req.ExistingCharts, relationshipsSnippet);

            var userPrompt = string.IsNullOrWhiteSpace(req.Prompt)
                ? "Generate a comprehensive multi-page report covering all available data with KPIs, charts, and tables."
                : req.Prompt;

            if (!string.IsNullOrWhiteSpace(req.ExistingCharts))
            {
                userPrompt += "\n\n## Existing Charts to Redesign:\n" + req.ExistingCharts;
            }

            // ── Stream AI response with auto-continuation ────────────
            // A multi-page report plan can run well past the model's per-call
            // output cap (~4-8k tokens). When the stream ends with truncated /
            // unbalanced JSON we re-prompt the model with the partial answer
            // already in conversation history and ask it to "continue exactly
            // from where you stopped" — concatenating the new chunks into the
            // same SSE feed so the client never sees the boundary.
            //
            // Hard caps: at most 10 continuation rounds and ~250 KB of JSON.
            // That comfortably covers the 100 KB target while preventing a
            // runaway loop if the model misbehaves.
            const int MaxRounds = 10;
            const int MaxTotalChars = 250_000;
            const int PerCallMaxTokens = 8000;

            var history = new List<(string role, string content)>();
            var accumulated = new StringBuilder();
            var currentUserPrompt = userPrompt;

            // System prompt is sent on every round but charged once — it's
            // the same string. Initial user prompt is also charged once;
            // continuation prompts are added below as we issue them.
            inputChars += (systemPrompt?.Length ?? 0) + currentUserPrompt.Length;

            for (int round = 0; round < MaxRounds; round++)
            {
                if (round > 0)
                {
                    inputChars += currentUserPrompt.Length;
                }
                var roundText = new StringBuilder();
                await foreach (var chunk in _cohere.StreamChatAsync(currentUserPrompt, history, systemPrompt, maxTokens: PerCallMaxTokens))
                {
                    roundText.Append(chunk);
                    accumulated.Append(chunk);
                    outputChars += chunk?.Length ?? 0;
                    await Response.WriteAsync($"data: {{\"text\":\"{EscapeJson(chunk)}\"}}\n\n");
                    await Response.Body.FlushAsync();
                    if (accumulated.Length >= MaxTotalChars) break;
                }

                if (accumulated.Length >= MaxTotalChars) break;
                if (roundText.Length == 0) break; // model produced nothing — stop

                // If the JSON looks complete (balanced braces, ends with `}`),
                // we're done. Otherwise feed the partial answer back and ask
                // the model to keep going.
                if (LooksComplete(accumulated.ToString())) break;

                history.Add(("user", currentUserPrompt));
                history.Add(("assistant", roundText.ToString()));
                currentUserPrompt =
                    "Continue the JSON output from EXACTLY the character where you stopped. " +
                    "Do NOT repeat any text already produced. Do NOT wrap the continuation in markdown or code fences. " +
                    "Do NOT restart the JSON. Emit only the remaining characters needed to finish the document, " +
                    "ending with the closing `}` of the top-level object.";
            }

            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            await Response.WriteAsync($"data: {{\"error\":\"{EscapeJson(ex.Message)}\"}}\n\n");
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
        }
        finally
        {
            // Record token usage (estimate: 1 token ≈ 4 chars for input + output)
            // mirroring the formula used by ChatController. Wrapped so any
            // bookkeeping failure never breaks the streaming response.
            if (orgId.HasValue && orgId.Value > 0)
            {
                try
                {
                    var estimatedTokens = (inputChars + outputChars) / 4;
                    if (estimatedTokens > 0)
                        await _tokenBudget.RecordUsageAsync(orgId.Value, userId, estimatedTokens);
                }
                catch { /* best-effort accounting */ }
            }
        }
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    /// <summary>
    /// Best-effort check that the streamed JSON document is structurally complete.
    /// Walks the text once, ignoring braces/brackets that appear inside string
    /// literals (so the SQL inside `"dataQuery": "..."` doesn't confuse the count),
    /// and returns true when every `{` and `[` has a matching `}` / `]` AND the
    /// last non-whitespace character is `}` or `]`.
    /// </summary>
    private static bool LooksComplete(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        int curly = 0, square = 0;
        bool sawStructure = false;
        bool inString = false;
        bool escape = false;
        char lastNonWs = '\0';

        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c)) lastNonWs = c;

            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': curly++; sawStructure = true; break;
                case '}': curly--; break;
                case '[': square++; sawStructure = true; break;
                case ']': square--; break;
            }
        }

        return sawStructure
            && !inString
            && curly == 0
            && square == 0
            && (lastNonWs == '}' || lastNonWs == ']');
    }

    private static string BuildRelationshipsSnippet(IReadOnlyList<RelationshipInfo>? rels)
    {
        if (rels == null || rels.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Known Table Relationships (use these for JOINs)");
        sb.AppendLine("The left-hand column is a foreign key referencing the right-hand column. Prefer these when writing multi-table queries — do NOT invent joins that are not listed here.");
        foreach (var r in rels.Take(40))
        {
            sb.AppendLine($"- {r.FromTable}.{r.FromColumn} -> {r.ToTable}.{r.ToColumn}" + (string.IsNullOrEmpty(r.Source) ? "" : $"  (source: {r.Source})"));
        }
        return sb.ToString();
    }
}
