using AIInsights.Data;
using AIInsights.Filters;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Security.Claims;

namespace AIInsights.Controllers;

[Authorize]
public class ReportController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWorkspacePermissionService _permissions;
    private readonly IQueryExecutionService _queryService;
    private readonly JwtService _jwt;

    public ReportController(AppDbContext db, IWorkspacePermissionService permissions, IQueryExecutionService queryService, JwtService jwt)
    {
        _db = db;
        _permissions = permissions;
        _queryService = queryService;
        _jwt = jwt;
    }

    [AllowAnonymous]
    [HttpGet("/report/view/{guid}")]
    public async Task<IActionResult> ViewReport(string guid)
    {
        var report = await _db.Reports
            .Include(r => r.Workspace)
            .Include(r => r.Datasource)
            .Include(r => r.Agent)
            .FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        // Anonymous users can only view published reports
        if (User.Identity?.IsAuthenticated != true && report.Status != "Published")
            return Redirect("/access-denied?statusCode=401");

        // Anonymous viewers of a Published report MUST present a valid embed
        // token (?t=…) signed by us. The bare /report/view/{guid} URL no longer
        // grants access — it would otherwise render the page chrome and then
        // every chart would 401 from /api/reports/public/.../data.
        if (User.Identity?.IsAuthenticated != true)
        {
            var embedToken = Request.Query["t"].ToString();
            if (string.IsNullOrWhiteSpace(embedToken))
                return Redirect("/access-denied?statusCode=401&reason=embed-token-required");

            var embedClaims = _jwt.ValidateEmbedToken(embedToken);
            if (embedClaims == null
                || !string.Equals(embedClaims.ReportGuid, report.Guid, StringComparison.Ordinal)
                || embedClaims.TokenVersion != report.EmbedTokenVersion)
            {
                return Redirect("/access-denied?statusCode=401&reason=embed-token-invalid");
            }
        }

        // Authenticated users must have workspace access, shared report access, or report must be Published
        string? workspaceRole = null;
        if (User.Identity?.IsAuthenticated == true && report.WorkspaceId > 0)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (report.Status != "Published")
            {
                var hasSharedAccess = await _db.SharedReports
                    .AnyAsync(sr => sr.ReportId == report.Id && sr.UserId == userId);
                if (!hasSharedAccess && !await _permissions.CanViewAsync(report.WorkspaceId, userId))
                {
                    var appUser = await _db.Users.FindAsync(userId);
                    if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                        return Redirect("/access-denied?statusCode=403");
                }
            }
            workspaceRole = await _permissions.GetRoleAsync(report.WorkspaceId, userId);
        }

        ViewBag.ReportGuid = report.Guid;
        ViewBag.ReportName = report.Name;
        ViewBag.CanvasJson = report.CanvasJson;
        ViewBag.WorkspaceGuid = report.Workspace?.Guid;
        ViewBag.WorkspaceName = report.Workspace?.Name;
        ViewBag.DatasourceName = report.Datasource?.Name;
        ViewBag.DatasourceId = report.DatasourceId;
        ViewBag.DatasourceType = report.Datasource?.Type;
        ViewBag.AgentName = report.Agent?.Name;
        ViewBag.Status = report.Status;
        ViewBag.UpdatedAt = report.UpdatedAt ?? report.CreatedAt;
        ViewBag.WorkspaceRole = workspaceRole;

        // Embed mode: clean, chrome-less view for iframe embedding (?embed=1).
        // Only honored for Published reports so private drafts can never be silently embedded.
        ViewBag.EmbedMode = report.Status == "Published"
            && (Request.Query.ContainsKey("embed") || Request.Query.ContainsKey("embedded"));

        return View("~/Views/Report/View.cshtml");
    }

    [AllowAnonymous]
    [HttpGet("/report/share/{token}")]
    public async Task<IActionResult> AcceptShareLink(string token)
    {
        var report = await _db.Reports
            .Include(r => r.Workspace)
            .FirstOrDefaultAsync(r => r.ShareToken == token);
        if (report == null) return NotFound("Invalid or expired share link.");

        // If user is authenticated, grant them report-level access (not workspace-level)
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var already = await _db.SharedReports
                .AnyAsync(sr => sr.ReportId == report.Id && sr.UserId == userId);
            if (!already)
            {
                _db.SharedReports.Add(new SharedReport
                {
                    ReportId = report.Id,
                    UserId = userId
                });
                await _db.SaveChangesAsync();
            }
        }

        return Redirect("/report/view/" + report.Guid);
    }

    [HttpGet("/api/reports/{guid}/share")]
    public async Task<IActionResult> GetShareToken(string guid)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        return Ok(new { shareToken = report.ShareToken });
    }

    [HttpPost("/api/reports/{guid}/share")]
    [RequireActiveSubscription]
    public async Task<IActionResult> GenerateShareToken(string guid)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to share reports." });

        if (string.IsNullOrEmpty(report.ShareToken))
        {
            report.ShareToken = Guid.NewGuid().ToString("N");
            await _db.SaveChangesAsync();
        }

        return Ok(new { shareToken = report.ShareToken });
    }

    // Mint a signed embed token bound to this report's datasource and the set
    // of tables actually referenced by its canvas. Editor or Admin permission
    // is required, so anonymous viewers can never request a token themselves —
    // they only receive one when an authorised user shares the URL with them.
    [HttpPost("/api/reports/{guid}/embed-token")]
    [RequireActiveSubscription]
    public async Task<IActionResult> CreateEmbedToken(string guid, [FromBody] CreateEmbedTokenRequest? req)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        // Anyone with at least Viewer access to the workspace (or shared access)
        // can mint an embed token for a Published report. The report is already
        // publicly viewable, so issuing a signed link does not escalate access —
        // it just lets non-editors copy the public/embed URL too. Editor remains
        // required for unpublished drafts.
        if (report.WorkspaceId > 0)
        {
            var canEdit = await _permissions.CanEditAsync(report.WorkspaceId, userId);
            if (!canEdit)
            {
                if (report.Status != "Published")
                    return StatusCode(403, new { error = "You need Editor or Admin role to create embed tokens for unpublished reports." });
                var canView = await _permissions.CanViewAsync(report.WorkspaceId, userId);
                var hasSharedAccess = await _db.SharedReports
                    .AnyAsync(sr => sr.ReportId == report.Id && sr.UserId == userId);
                if (!canView && !hasSharedAccess)
                    return StatusCode(403, new { error = "You do not have access to this report." });
            }
        }
        if (report.Status != "Published")
            return BadRequest(new { error = "Only Published reports can be embedded. Publish the report first." });
        if (report.DatasourceId == null)
            return BadRequest(new { error = "Report has no datasource bound." });

        // Derive the table allow-list from the report's saved canvas. Each
        // chart node carries a `datasetName` identifying the table it queries.
        var tables = ExtractTableNamesFromCanvas(report.CanvasJson);

        var expiresInDays = Math.Clamp(req?.ExpiresInDays ?? 30, 1, 365);
        var token = _jwt.GenerateEmbedToken(
            report.Guid,
            report.DatasourceId.Value,
            report.EmbedTokenVersion,
            tables,
            expiresInDays);
        var expiresAt = DateTime.UtcNow.AddDays(expiresInDays);

        var origin = $"{Request.Scheme}://{Request.Host}";
        var publicUrl = $"{origin}/report/view/{report.Guid}?t={Uri.EscapeDataString(token)}";
        var embedUrl = $"{origin}/report/view/{report.Guid}?embed=1&t={Uri.EscapeDataString(token)}";

        return Ok(new
        {
            token,
            expiresAt,
            publicUrl,
            embedUrl,
            tables,
            tokenVersion = report.EmbedTokenVersion
        });
    }

    // Bumps the report's EmbedTokenVersion, immediately invalidating every
    // outstanding embed token for this report.
    [HttpPost("/api/reports/{guid}/embed-token/revoke")]
    [RequireActiveSubscription]
    public async Task<IActionResult> RevokeEmbedTokens(string guid)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to revoke embed tokens." });

        report.EmbedTokenVersion += 1;
        report.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true, tokenVersion = report.EmbedTokenVersion });
    }

    // Strip surrounding [ ] " ` quotes from each path segment and lowercase
    // for a case-insensitive comparison key. Preserves multi-part identifiers
    // (e.g. "dbo.MyTable") so the allow-list and the SQL match shape-for-shape.
    private static string NormalizeTableName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var cleaned = parts.Select(p => p.Trim().Trim('[', ']', '"', '`').Trim());
        return string.Join(".", cleaned).ToLowerInvariant();
    }

    // Walk the canvas JSON and collect every distinct datasetName referenced
    // by chart-shaped nodes. The structure is intentionally tolerant — any
    // string-valued "datasetName" / "DatasetName" is accepted regardless of
    // depth so future canvas schema tweaks don't silently shrink the allow-list.
    private static List<string> ExtractTableNamesFromCanvas(string? canvasJson)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(canvasJson)) return result.ToList();
        try
        {
            var token = Newtonsoft.Json.Linq.JToken.Parse(canvasJson);
            // Recursive descent — match any property named datasetName at any depth.
            foreach (var v in token.SelectTokens("$..datasetName"))
            {
                AddCanvasTableName(result, v);
            }
            // JSONPath in Newtonsoft is case-sensitive — also pick up DatasetName.
            foreach (var v in token.SelectTokens("$..DatasetName"))
            {
                AddCanvasTableName(result, v);
            }
        }
        catch { /* tolerant: bad canvas JSON => empty allow-list, mint will block */ }
        return result.ToList();
    }

    private static void AddCanvasTableName(HashSet<string> sink, Newtonsoft.Json.Linq.JToken v)
    {
        string? name = null;
        if (v is Newtonsoft.Json.Linq.JValue jv && jv.Type == Newtonsoft.Json.Linq.JTokenType.String)
        {
            name = jv.ToString();
        }
        else if (v is Newtonsoft.Json.Linq.JObject jo)
        {
            name = jo.Value<string>("name") ?? jo.Value<string>("Name");
        }
        if (!string.IsNullOrWhiteSpace(name))
            sink.Add(name.Trim());
    }

    // Anonymous chart-data endpoint scoped to a single Published report's bound datasource.
    // The chart renderer uses this when the viewer is not signed in (e.g. public link or
    // iframe embed) so charts can fetch their data without hitting the authenticated
    // /api/data/execute endpoint, which would otherwise redirect anonymous callers to
    // login and break JSON parsing on the client.
    [AllowAnonymous]
    [HttpPost("/api/reports/public/{reportGuid}/data")]
    public async Task<IActionResult> PublicExecuteQuery(string reportGuid, [FromBody] ExecuteQueryRequest req)
    {
        if (req == null) return BadRequest(new { success = false, error = "Missing request body." });

        // ── Embed-token gate ────────────────────────────────────────────────
        // Accept token from Authorization: Bearer <jwt> header (preferred for
        // SPA fetches) or ?t=<jwt> query string (used by the iframe `src` so
        // even uncontrolled embedders work).
        var rawToken = "";
        var authHeader = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            rawToken = authHeader.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrEmpty(rawToken))
            rawToken = Request.Query["t"].ToString();
        if (string.IsNullOrWhiteSpace(rawToken))
            return Unauthorized(new { success = false, error = "Embed token required." });

        var claims = _jwt.ValidateEmbedToken(rawToken);
        if (claims == null)
            return Unauthorized(new { success = false, error = "Embed token is invalid or expired." });
        if (!string.Equals(claims.ReportGuid, reportGuid, StringComparison.Ordinal))
            return StatusCode(403, new { success = false, error = "Token does not belong to this report." });

        var report = await _db.Reports
            .Include(r => r.Datasource)
            .FirstOrDefaultAsync(r => r.Guid == reportGuid);
        if (report == null) return NotFound(new { success = false, error = "Report not found." });

        // Token revocation check: if the report's token version was bumped after
        // the JWT was minted, reject — even though the signature is still valid.
        if (claims.TokenVersion != report.EmbedTokenVersion)
            return Unauthorized(new { success = false, error = "Embed token has been revoked." });

        // Only Published reports may serve data anonymously.
        if (report.Status != "Published")
            return StatusCode(403, new { success = false, error = "This report is not published." });

        // The caller may only query the report's bound datasource — never an arbitrary one.
        if (!req.DatasourceId.HasValue || report.DatasourceId == null || req.DatasourceId.Value != report.DatasourceId.Value)
            return StatusCode(403, new { success = false, error = "Datasource is not bound to this report." });

        // The token's `dsid` must also match — defence-in-depth in case the
        // report's bound datasource was rebound after the token was minted.
        if (claims.DatasourceId != report.DatasourceId.Value)
            return StatusCode(403, new { success = false, error = "Token does not match this report's datasource." });

        var ds = report.Datasource;
        if (ds == null) return Ok(new { success = false, data = Array.Empty<object>(), rowCount = 0, error = "Report has no datasource." });

        var query = (req.Query ?? "").Trim();
        query = QueryExecutionService.StripSqlComments(query);
        if (!string.IsNullOrEmpty(query) && !query.TrimStart().StartsWith("EVALUATE", StringComparison.OrdinalIgnoreCase))
        {
            var firstStatement = query.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(firstStatement)) query = firstStatement;
        }

        // Read-only guard — block any write operations even on the public path.
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            query.ToUpperInvariant(), @"\s+", " ").Trim();
        var writeOps = new[] { "INSERT","UPDATE","DELETE","DROP","CREATE","ALTER",
                               "TRUNCATE","EXEC","EXECUTE","MERGE","CALL","GRANT",
                               "REVOKE","REPLACE","UPSERT","ATTACH","DETACH" };
        var firstToken = System.Text.RegularExpressions.Regex.Split(
            normalized.TrimStart(), @"[\s(;]+").FirstOrDefault() ?? "";
        var isDaxOrDmv = firstToken == "EVALUATE"
            || firstToken == "REST_API"
            || firstToken == "FILE_URL"
            || (firstToken == "SELECT" && normalized.Contains("$SYSTEM."));
        if (!isDaxOrDmv &&
            (writeOps.Contains(firstToken) ||
             writeOps.Any(kw => System.Text.RegularExpressions.Regex.IsMatch(normalized, $@"\b{kw}\b"))))
        {
            return BadRequest(new
            {
                success = false,
                error = $"Write operation \"{firstToken}\" is not permitted on a public report."
            });
        }

        // Table allow-list — every FROM/JOIN target in the SQL must appear in
        // the token's `tables` claim (which was derived from the report's
        // CanvasJson at mint time). Skipped for DAX/DMV/REST/File paths since
        // those don't reference user-supplied tables. Empty allow-list = block.
        if (!isDaxOrDmv
            && !QueryExecutionService.RestApiTypes.Contains(ds?.Type ?? "")
            && !QueryExecutionService.FileUrlTypes.Contains(ds?.Type ?? ""))
        {
            var allowed = new HashSet<string>(
                claims.Tables.Select(NormalizeTableName).Where(s => !string.IsNullOrEmpty(s)),
                StringComparer.OrdinalIgnoreCase);
            if (allowed.Count == 0)
                return StatusCode(403, new { success = false, error = "Token has no permitted tables." });

            // Match identifiers after FROM/JOIN. Captures `dbo.MyTable`,
            // `[dbo].[MyTable]`, `"public"."MyTable"`, `MyTable`, etc. Stops
            // at the first whitespace/paren/comma/semicolon.
            var refs = System.Text.RegularExpressions.Regex.Matches(
                query,
                @"\b(?:FROM|JOIN)\s+([\[\""\w\.\]\""`]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in refs)
            {
                var table = NormalizeTableName(m.Groups[1].Value);
                if (string.IsNullOrEmpty(table)) continue;
                if (!allowed.Contains(table))
                {
                    return StatusCode(403, new
                    {
                        success = false,
                        error = $"Table \"{table}\" is not permitted by this embed token."
                    });
                }
            }
        }

        // REST / File-URL datasources bypass SQL execution.
        if (QueryExecutionService.RestApiTypes.Contains(ds.Type ?? ""))
        {
            var apiResult = await _queryService.ExecuteRestApiAsync(ds);
            return Ok(new { success = apiResult.Success, data = apiResult.Data, rowCount = apiResult.RowCount, error = apiResult.Error });
        }
        if (QueryExecutionService.FileUrlTypes.Contains(ds.Type ?? ""))
        {
            var fileResult = await _queryService.ExecuteFileUrlAsync(ds);
            return Ok(new { success = fileResult.Success, data = fileResult.Data, rowCount = fileResult.RowCount, error = fileResult.Error });
        }

        var isPbi = QueryExecutionService.PowerBiTypes.Contains(ds.Type ?? "");
        var hasConnection = isPbi
            ? !string.IsNullOrWhiteSpace(ds.XmlaEndpoint)
            : !string.IsNullOrWhiteSpace(ds.ConnectionString);
        if (!hasConnection)
        {
            return Ok(new
            {
                success = false,
                data = Array.Empty<object>(),
                rowCount = 0,
                error = "Report's datasource is not connected."
            });
        }

        var result = await _queryService.ExecuteReadOnlyAsync(ds, query);
        return Ok(new { success = result.Success, data = result.Data, rowCount = result.RowCount, error = result.Error });
    }

    [HttpGet("/api/reports/shared")]
    public async Task<IActionResult> GetSharedWithMe()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var reports = await _db.SharedReports
            .Where(sr => sr.UserId == userId)
            .Include(sr => sr.Report)
                .ThenInclude(r => r!.Workspace)
            .Include(sr => sr.Report)
                .ThenInclude(r => r!.Datasource)
            .Include(sr => sr.Report)
                .ThenInclude(r => r!.Agent)
            .OrderByDescending(sr => sr.SharedAt)
            .Select(sr => new
            {
                sr.Report!.Id,
                sr.Report.Guid,
                sr.Report.Name,
                sr.Report.Status,
                workspaceName = sr.Report.Workspace != null ? sr.Report.Workspace.Name : null,
                datasourceName = sr.Report.Datasource != null ? sr.Report.Datasource.Name : null,
                agentName = sr.Report.Agent != null ? sr.Report.Agent.Name : null,
                sr.SharedAt
            })
            .ToListAsync();
        return Ok(reports);
    }

    [HttpGet("/api/reports")]
    public async Task<IActionResult> GetByWorkspace([FromQuery] string workspaceGuid, [FromQuery] int? agentId = null, [FromQuery] string? agentGuid = null)
    {
        var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == workspaceGuid);
        if (ws == null) return NotFound();

        // Resolve agent scoping: if agentGuid is supplied, translate to id.
        int? resolvedAgentId = agentId;
        if (!resolvedAgentId.HasValue && !string.IsNullOrEmpty(agentGuid))
        {
            var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Guid == agentGuid || a.Id.ToString() == agentGuid);
            if (agent != null) resolvedAgentId = agent.Id;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (!await _permissions.CanViewReportsAsync(ws.Id, userId))
        {
            var appUser = await _db.Users.FindAsync(userId);
            if (appUser?.Role == "SuperAdmin")
            {
                return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });
            }
            if (appUser?.Role == "OrgAdmin")
            {
                if ((appUser.OrganizationId ?? 0) != ws.OrganizationId)
                    return StatusCode(403, new { error = "You do not have access to workspaces in other organizations." });
                // OrgAdmin within own org can proceed
            }
            else
            {
                return StatusCode(403, new { error = "You do not have access to this workspace." });
            }
        }

        var query = _db.Reports.Where(r => r.WorkspaceId == ws.Id);
        if (resolvedAgentId.HasValue)
        {
            // Agent-scoped: return only reports tied to this agent. Legacy reports without
            // an AgentId that were created against the agent's datasource are also included
            // so upsert lookups can still find them when migrating to per-agent scoping.
            var agentForFallback = await _db.Agents
                .Where(a => a.Id == resolvedAgentId.Value)
                .Select(a => new { a.DatasourceId })
                .FirstOrDefaultAsync();
            var agentDsId = agentForFallback?.DatasourceId;
            query = query.Where(r =>
                r.AgentId == resolvedAgentId.Value
                || (r.AgentId == null && agentDsId != null && r.DatasourceId == agentDsId));
        }

        var reports = await query
            .Include(r => r.Datasource)
            .Include(r => r.Agent)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.Guid,
                r.Name,
                r.Status,
                r.ChartIds,
                r.AgentId,
                datasourceName = r.Datasource != null ? r.Datasource.Name : null,
                agentName = r.Agent != null ? r.Agent.Name : null,
                r.CreatedBy,
                r.CreatedAt
            })
            .ToListAsync();

        return Ok(reports);
    }

    [HttpGet("/api/reports/{guid}")]
    public async Task<IActionResult> GetByGuid(string guid)
    {
        var report = await _db.Reports
            .Include(r => r.Datasource)
            .Include(r => r.Agent)
            .Include(r => r.Workspace)
            .FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        if (report.WorkspaceId > 0)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (!await _permissions.CanViewReportsAsync(report.WorkspaceId, userId))
            {
                // Check report-level shared access
                var hasSharedAccess = await _db.SharedReports
                    .AnyAsync(sr => sr.ReportId == report.Id && sr.UserId == userId);
                if (!hasSharedAccess)
                {
                    var appUser = await _db.Users.FindAsync(userId);
                    if (appUser?.Role == "SuperAdmin")
                    {
                        return StatusCode(403, new { error = "SuperAdmin does not have access to the AI Insights portal." });
                    }
                    else if (appUser?.Role == "OrgAdmin")
                    {
                        var ws = await _db.Workspaces.FindAsync(report.WorkspaceId);
                        if ((appUser.OrganizationId ?? 0) != (ws?.OrganizationId ?? 0))
                            return StatusCode(403, new { error = "You do not have access to workspaces in other organizations." });
                    }
                    else
                    {
                        return StatusCode(403, new { error = "You do not have access to this report." });
                    }
                }
            }
        }

        return Ok(new
        {
            report.Id,
            report.Guid,
            report.Name,
            report.Status,
            report.ChartIds,
            report.CanvasJson,
            workspaceGuid = report.Workspace?.Guid,
            workspaceName = report.Workspace?.Name,
            datasourceId = report.DatasourceId,
            datasourceName = report.Datasource?.Name,
            datasourceType = report.Datasource?.Type,
            agentId = report.AgentId,
            agentName = report.Agent?.Name,
            report.CreatedBy,
            report.CreatedAt
        });
    }

    [HttpPost("/api/reports")]
    [RequireActiveSubscription]
    public async Task<IActionResult> Create([FromBody] CreateReportRequest? req)
    {
        if (req?.WorkspaceGuid == null) return BadRequest(new { error = "workspaceGuid is required." });

        var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Guid == req.WorkspaceGuid);
        if (ws == null) return NotFound(new { error = "Workspace not found." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.CreatedBy ?? "";
        if (!await _permissions.CanEditAsync(ws.Id, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to create reports." });

        var name = (req.Name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name)) name = "Untitled Report";
        var chartIdsJson = req.ChartIds != null ? JsonConvert.SerializeObject(req.ChartIds) : null;

        // Upsert-by-name: if a report with the same name already exists in this workspace
        // for the same data source (and agent scope, when provided), overwrite it instead of
        // creating a duplicate. This preserves the 1-datasource → many-reports relationship:
        // distinct names bound to the same datasource produce new reports, while saving with
        // an existing name updates the matching report in place.
        var nameLower = name.ToLower();
        var existingQuery = _db.Reports.Where(r =>
            r.WorkspaceId == ws.Id &&
            r.DatasourceId == req.DatasourceId &&
            r.Name.ToLower() == nameLower);
        if (req.AgentId.HasValue)
            existingQuery = existingQuery.Where(r => r.AgentId == req.AgentId);
        var existing = await existingQuery.FirstOrDefaultAsync();

        if (existing != null)
        {
            // Overwrite existing report. Capture prior canvas as an auto-revision for history
            // parity with PUT /api/reports/{guid}.
            if (!string.IsNullOrEmpty(existing.CanvasJson) && existing.CanvasJson != req.CanvasJson)
            {
                _db.ReportRevisions.Add(new ReportRevision
                {
                    ReportId = existing.Id,
                    Kind = "Auto",
                    Name = null,
                    CanvasJson = existing.CanvasJson,
                    ReportName = existing.Name,
                    CreatedBy = userId,
                    CreatedAt = DateTime.UtcNow
                });

                // Trim auto-revisions to the most recent 20 per report.
                const int MaxAutoRevisions = 20;
                var autoCount = await _db.ReportRevisions
                    .CountAsync(r => r.ReportId == existing.Id && r.Kind == "Auto");
                if (autoCount >= MaxAutoRevisions)
                {
                    var stale = await _db.ReportRevisions
                        .Where(r => r.ReportId == existing.Id && r.Kind == "Auto")
                        .OrderByDescending(r => r.CreatedAt)
                        .Skip(MaxAutoRevisions - 1)
                        .ToListAsync();
                    _db.ReportRevisions.RemoveRange(stale);
                }
            }

            existing.DashboardId = req.DashboardId ?? existing.DashboardId;
            if (req.AgentId.HasValue) existing.AgentId = req.AgentId;
            existing.ChartIds = chartIdsJson ?? existing.ChartIds;
            existing.CanvasJson = req.CanvasJson ?? existing.CanvasJson;
            // DatasourceId is intentionally left as-is to preserve the binding the match was based on.
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(new { existing.Id, existing.Guid, existing.Name, existing.Status, overwritten = true });
        }

        var report = new Report
        {
            Name = name,
            WorkspaceId = ws.Id,
            DashboardId = req.DashboardId,
            DatasourceId = req.DatasourceId,
            AgentId = req.AgentId,
            ChartIds = chartIdsJson,
            CanvasJson = req.CanvasJson,
            Status = "Draft",
            CreatedBy = req.CreatedBy
        };

        _db.Reports.Add(report);
        await _db.SaveChangesAsync();

        return Ok(new { report.Id, report.Guid, report.Name, report.Status, overwritten = false });
    }

    [HttpPut("/api/reports/{guid}")]
    [RequireActiveSubscription]
    public async Task<IActionResult> Update(string guid, [FromBody] UpdateReportRequest req)
    {
        var report = await _db.Reports.Include(r => r.Workspace).FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var wsId = report.WorkspaceId;
        if (wsId > 0 && !await _permissions.CanEditAsync(wsId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to update reports." });

        if (req.Name != null) report.Name = req.Name;
        if (req.ChartIds != null) report.ChartIds = JsonConvert.SerializeObject(req.ChartIds);
        if (req.CanvasJson != null)
        {
            // Phase 34b B18: capture prior canvas state as an auto-revision before overwriting.
            if (!string.IsNullOrEmpty(report.CanvasJson) && report.CanvasJson != req.CanvasJson)
            {
                _db.ReportRevisions.Add(new ReportRevision
                {
                    ReportId = report.Id,
                    Kind = "Auto",
                    Name = null,
                    CanvasJson = report.CanvasJson,
                    ReportName = report.Name,
                    CreatedBy = userId,
                    CreatedAt = DateTime.UtcNow
                });

                // Trim auto-revisions to the most recent 20 per report.
                const int MaxAutoRevisions = 20;
                var autoCount = await _db.ReportRevisions
                    .CountAsync(r => r.ReportId == report.Id && r.Kind == "Auto");
                if (autoCount >= MaxAutoRevisions)
                {
                    var stale = await _db.ReportRevisions
                        .Where(r => r.ReportId == report.Id && r.Kind == "Auto")
                        .OrderByDescending(r => r.CreatedAt)
                        .Skip(MaxAutoRevisions - 1)
                        .ToListAsync();
                    _db.ReportRevisions.RemoveRange(stale);
                }
            }
            report.CanvasJson = req.CanvasJson;
        }
        if (req.Status != null) report.Status = req.Status;
        report.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { report.Id, report.Guid, report.Name, report.Status });
    }

    [HttpPost("/api/reports/{guid}/publish")]
    [RequireActiveSubscription]
    public async Task<IActionResult> Publish(string guid)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to publish reports." });

        report.Status = "Published";
        await _db.SaveChangesAsync();
        return Ok(new { report.Guid, report.Status });
    }

    [HttpDelete("/api/reports/{guid}")]
    [RequireActiveSubscription]
    public async Task<IActionResult> Delete(string guid)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanDeleteAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "Only Admins can delete reports." });

        // Remove shared report records
        var sharedRecords = await _db.SharedReports.Where(sr => sr.ReportId == report.Id).ToListAsync();
        _db.SharedReports.RemoveRange(sharedRecords);

        _db.Reports.Remove(report);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ─── Phase 34b — Revisions & Named Snapshots ─────────────────────────────

    [HttpGet("/api/reports/{guid}/revisions")]
    public async Task<IActionResult> GetRevisions(string guid, [FromQuery] string kind = "Auto")
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanViewReportsAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You do not have access to this report." });

        var list = await _db.ReportRevisions
            .Where(r => r.ReportId == report.Id && r.Kind == kind)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.Kind, r.Name, r.ReportName, r.CreatedBy, r.CreatedAt })
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("/api/reports/{guid}/snapshots")]
    [RequireActiveSubscription]
    public async Task<IActionResult> CreateSnapshot(string guid, [FromBody] CreateSnapshotRequest req)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to create snapshots." });

        if (string.IsNullOrWhiteSpace(req?.Name))
            return BadRequest(new { error = "Snapshot name is required." });

        var snap = new ReportRevision
        {
            ReportId = report.Id,
            Kind = "Snapshot",
            Name = req.Name.Trim(),
            CanvasJson = req.CanvasJson ?? report.CanvasJson,
            ReportName = report.Name,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.ReportRevisions.Add(snap);
        await _db.SaveChangesAsync();

        return Ok(new { snap.Id, snap.Kind, snap.Name, snap.CreatedAt });
    }

    [HttpPost("/api/reports/{guid}/revisions/{revId}/restore")]
    [RequireActiveSubscription]
    public async Task<IActionResult> RestoreRevision(string guid, int revId)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to restore revisions." });

        var rev = await _db.ReportRevisions
            .FirstOrDefaultAsync(r => r.Id == revId && r.ReportId == report.Id);
        if (rev == null) return NotFound(new { error = "Revision not found." });

        // Capture current state as an auto-revision before restoring.
        if (!string.IsNullOrEmpty(report.CanvasJson))
        {
            _db.ReportRevisions.Add(new ReportRevision
            {
                ReportId = report.Id,
                Kind = "Auto",
                Name = "Before restore",
                CanvasJson = report.CanvasJson,
                ReportName = report.Name,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        report.CanvasJson = rev.CanvasJson;
        report.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { success = true, canvasJson = rev.CanvasJson });
    }

    [HttpDelete("/api/reports/{guid}/revisions/{revId}")]
    [RequireActiveSubscription]
    public async Task<IActionResult> DeleteRevision(string guid, int revId)
    {
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        if (report.WorkspaceId > 0 && !await _permissions.CanEditAsync(report.WorkspaceId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to delete revisions." });

        var rev = await _db.ReportRevisions
            .FirstOrDefaultAsync(r => r.Id == revId && r.ReportId == report.Id);
        if (rev == null) return NotFound();

        _db.ReportRevisions.Remove(rev);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}

public class CreateSnapshotRequest
{
    public string? Name { get; set; }
    public string? CanvasJson { get; set; }
}

public class CreateEmbedTokenRequest
{
    public int? ExpiresInDays { get; set; }
}

public class CreateReportRequest
{
    public string? WorkspaceGuid { get; set; }
    public string? Name { get; set; }
    public int? DashboardId { get; set; }
    public int? DatasourceId { get; set; }
    public int? AgentId { get; set; }
    public List<string>? ChartIds { get; set; }
    public string? CanvasJson { get; set; }
    public string? CreatedBy { get; set; }
}

public class UpdateReportRequest
{
    public string? Name { get; set; }
    public List<string>? ChartIds { get; set; }
    public string? CanvasJson { get; set; }
    public string? Status { get; set; }
}
