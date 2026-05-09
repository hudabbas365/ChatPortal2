using AIInsights.Data;
using AIInsights.Filters;
using AIInsights.Models;
using AIInsights.Services;
using AIInsights.Services.Datasources;
using AIInsights.Services.Transforms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace AIInsights.Controllers;

[Authorize]
[Route("api/datasources")]
[ApiController]
public class DatasourceController : ControllerBase
{
    private const int DefaultPreviewLoadRows = 5000;
    private const int MaxPreviewLoadRows = 10000;
    private const int TransformPreviewPageSize = 1000;

    private readonly List<string> _datasourceTypeNames;

    private readonly AppDbContext _db;
    private readonly IQueryExecutionService _queryService;
    private readonly IWorkspacePermissionService _permissions;
    private readonly IEncryptionService _encryption;
    private readonly IRelationshipService _relationships;
    private readonly IQueryCacheInvalidator _cacheInvalidator;
    private readonly IEnumerable<IDatasourceTypeService> _datasourceServices;
    private readonly IAiInsightTransformService _transformService;
    private readonly ITransformValidationService _transformValidation;
    private readonly ITransformDraftService _transformDrafts;
    private readonly ITransformPreviewService _transformPreview;
    private readonly ITransformPublishService _transformPublish;
    private readonly ITransformSuggestionService _transformSuggestion;

    public DatasourceController(
        AppDbContext db,
        IQueryExecutionService queryService,
        IWorkspacePermissionService permissions,
        IEncryptionService encryption,
        IRelationshipService relationships,
        IQueryCacheInvalidator cacheInvalidator,
        IEnumerable<IDatasourceTypeService> datasourceServices,
        IAiInsightTransformService transformService,
        ITransformValidationService transformValidation,
        ITransformDraftService transformDrafts,
        ITransformPreviewService transformPreview,
        ITransformPublishService transformPublish,
        ITransformSuggestionService transformSuggestion)
    {
        _db = db;
        _queryService = queryService;
        _permissions = permissions;
        _encryption = encryption;
        _relationships = relationships;
        _cacheInvalidator = cacheInvalidator;
        _datasourceServices = datasourceServices;
        _transformService = transformService;
        _transformValidation = transformValidation;
        _transformDrafts = transformDrafts;
        _transformPreview = transformPreview;
        _transformPublish = transformPublish;
        _transformSuggestion = transformSuggestion;

        // Build the allowed-type list from the registered IDatasourceTypeService instances so that
        // adding a new service automatically surfaces its type in the UI without touching this file.
        _datasourceTypeNames = datasourceServices
            .SelectMany(s => s.SupportedTypeStrings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IDatasourceTypeService? ResolveTypeService(string? type) =>
        _datasourceServices.FirstOrDefault(s => s.CanHandle(type));

    // Manual cache refresh — flushes every cached query result for this datasource so the
    // next request re-runs against the live database. Surfaced to the UI via a refresh
    // button on the datasource details modal.
    [HttpPost("{guid}/refresh-cache")]
    public async Task<IActionResult> RefreshCache(string guid)
    {
        Datasource? ds = null;
        if (int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        ds ??= await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;
        if (appUser?.Role != "SuperAdmin" && callerOrgId > 0 && ds.OrganizationId != callerOrgId)
            return StatusCode(403, new { error = "You do not have access to this datasource." });

        if (ds.WorkspaceId.HasValue && ds.WorkspaceId.Value > 0
            && !await _permissions.CanViewAsync(ds.WorkspaceId.Value, userId)
            && appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
            return StatusCode(403, new { error = "You do not have access to this datasource." });

        _cacheInvalidator.InvalidateDatasource(ds.Id);
        return Ok(new { success = true, datasourceId = ds.Id, datasourceGuid = ds.Guid });
    }

    private async Task<int> ResolveOrganizationIdAsync(int supplied, string? userId)
    {
        if (supplied > 0) return supplied;
        if (!string.IsNullOrEmpty(userId))
        {
            var userOrgId = await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.OrganizationId)
                .FirstOrDefaultAsync();
            if (userOrgId.HasValue && userOrgId.Value > 0) return userOrgId.Value;
        }
        var org = new Organization { Name = "Default Organization" };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(userId))
        {
            var userEntity = await _db.Users.FindAsync(userId);
            if (userEntity != null)
            {
                userEntity.OrganizationId = org.Id;
                await _db.SaveChangesAsync();
            }
        }

        return org.Id;
    }

    [HttpGet("types")]
    public async Task<IActionResult> GetTypes([FromServices] IDatasourceTypeRegistry registry)
    {
        var names = await registry.GetTypeNamesAsync();
        // Fall back to in-memory list when the DB registry is empty (e.g. a
        // brand-new deployment whose first request races the seeder).
        return Ok(names.Count > 0 ? names : (IReadOnlyList<string>)_datasourceTypeNames);
    }

    /// <summary>
    /// Rich datasource type catalog including the connection-parameter schema
    /// for each registered type. Drives the Add-Datasource modal in the
    /// Transform Workbench AND the workspace wizard, both consuming this
    /// single DB-backed source of truth (managed in the SuperAdmin app).
    /// </summary>
    [HttpGet("type-schemas")]
    public async Task<IActionResult> GetTypeSchemas([FromServices] IDatasourceTypeRegistry registry)
    {
        var schemas = await registry.GetSchemasAsync();
        var result = schemas.Select(s => new
        {
            type = s.Type,
            displayName = s.DisplayName,
            description = s.Description,
            icon = s.Icon,
            requiresGateway = s.RequiresGateway,
            gatewayHelp = s.GatewayHelp,
            sortOrder = s.SortOrder,
            parameters = s.Parameters.Select(p => new
            {
                key = p.Key,
                label = p.Label,
                type = p.Type,
                placeholder = p.Placeholder,
                required = p.Required,
                options = p.Options,
                help = p.Help
            }).ToList()
        }).ToList();

        // Fallback to the in-memory IDatasourceTypeService.Parameters when the
        // DB hasn't been seeded yet (very first boot, or local dev without
        // migrations applied) so the UI still works.
        if (result.Count == 0)
        {
            var fallback = _datasourceServices
                .SelectMany(s => s.SupportedTypeStrings.Select(t => new
                {
                    type = t,
                    displayName = t,
                    description = (string?)null,
                    icon = (string?)null,
                    requiresGateway = false,
                    gatewayHelp = (string?)null,
                    sortOrder = 0,
                    parameters = s.Parameters.Select(p => new
                    {
                        key = p.Key,
                        label = p.Label,
                        type = p.Type,
                        placeholder = p.Placeholder,
                        required = p.Required,
                        options = (IReadOnlyList<string>?)p.Options,
                        help = p.Help
                    }).ToList()
                }))
                .ToList();
            return Ok(fallback);
        }

        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int organizationId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        // Org sandbox: non-SuperAdmins are always scoped to their own org.
        if (appUser?.Role != "SuperAdmin")
        {
            if (callerOrgId <= 0)
                return StatusCode(403, new { error = "User is not assigned to an organization." });
            organizationId = callerOrgId;
        }

        var isOrgLevel = appUser?.Role == "OrgAdmin" || appUser?.Role == "SuperAdmin";

        var query = _db.Datasources.Where(d => d.OrganizationId == organizationId);

        if (!isOrgLevel)
        {
            query = query.Where(d =>
                !d.WorkspaceId.HasValue ||
                _db.Workspaces.Any(w => w.Id == d.WorkspaceId && w.OwnerId == userId) ||
                _db.WorkspaceUsers.Any(wu => wu.WorkspaceId == d.WorkspaceId && wu.UserId == userId));
        }

        var datasources = await query.ToListAsync();
        var result = datasources.Select(d => new
        {
            d.Id,
            d.Guid,
            d.Name,
            d.Type,
            ConnectionString = MaskConnectionString(_encryption.Decrypt(d.ConnectionString)),
            DbUser = !string.IsNullOrEmpty(d.DbUser) ? "••••••" : null,
            DbPassword = !string.IsNullOrEmpty(d.DbPassword) ? "••••••" : null,
            d.SelectedTables,
            XmlaEndpoint = !string.IsNullOrEmpty(d.XmlaEndpoint) ? "••••••" : null,
            d.TransformEnabled,
            d.TransformToml,
            d.OrganizationId,
            d.WorkspaceId,
            d.CreatedAt
        });
        return Ok(result);
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] DatasourceRequest req)
    {
        var type = req.Type ?? "SQL Server";
        var svc = ResolveTypeService(type);
        if (svc == null)
            return BadRequest(new { connected = false, error = $"Datasource type '{type}' is not supported." });

        var info = new DatasourceConnectionInfo
        {
            Type = type,
            ConnectionString = req.ConnectionString,
            DbUser = req.DbUser,
            DbPassword = req.DbPassword,
            XmlaEndpoint = req.XmlaEndpoint,
            MicrosoftAccountTenantId = req.MicrosoftAccountTenantId,
            ApiUrl = req.ApiUrl,
            ApiKey = req.ApiKey,
            ApiMethod = req.ApiMethod
        };

        var (ok, error) = await svc.TestConnectionAsync(info);
        if (!ok)
            return BadRequest(new { connected = false, error = error ?? "Connection failed." });
        return Ok(new { connected = true });
    }

    [HttpPost]
    [RequireActiveSubscription]
    public async Task<IActionResult> Create([FromBody] DatasourceRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";

        if (req.WorkspaceId.HasValue && req.WorkspaceId.Value > 0)
        {
            if (!await _permissions.CanEditAsync(req.WorkspaceId.Value, userId))
                return StatusCode(403, new { error = "You need Editor or Admin role to create datasources." });
        }

        var orgId = await ResolveOrganizationIdAsync(req.OrganizationId, userId);

        // Power BI requires an XMLA endpoint
        if (string.Equals(req.Type, "Power BI", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(req.XmlaEndpoint))
            return BadRequest(new { error = "XMLA Endpoint is required for Power BI datasources." });

        var ds = new Datasource
        {
            Name = req.Name ?? "New Datasource",
            Type = req.Type ?? "SQL Server",
            ConnectionString = _encryption.Encrypt(req.ConnectionString ?? ""),
            DbUser = _encryption.Encrypt(req.DbUser ?? ""),
            DbPassword = _encryption.Encrypt(req.DbPassword ?? ""),
            XmlaEndpoint = _encryption.Encrypt(req.XmlaEndpoint ?? ""),
            MicrosoftAccountTenantId = _encryption.Encrypt(req.MicrosoftAccountTenantId ?? ""),
            ApiUrl = _encryption.Encrypt(req.ApiUrl ?? ""),
            ApiKey = _encryption.Encrypt(req.ApiKey ?? ""),
            ApiMethod = req.ApiMethod ?? "GET",
            OrganizationId = orgId,
            WorkspaceId = req.WorkspaceId
        };
        _db.Datasources.Add(ds);
        await _db.SaveChangesAsync();

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "datasource_created",
            Description = $"Datasource '{ds.Name}' ({ds.Type}) connected.",
            UserId = userId,
            OrganizationId = orgId
        });
        await _db.SaveChangesAsync();

        return Ok(new { ds.Id, ds.Guid, ds.Name, ds.Type, ds.OrganizationId });
    }

    [HttpGet("{id}/fields")]
    public async Task<IActionResult> GetFields(int id)
    {
        var ds = await _db.Datasources.FindAsync(id);
        if (ds == null) return NotFound();

        var access = await EnsureViewAccessAsync(ds);
        if (access != null) return access;

        var svc = ResolveTypeService(ds.Type);
        if (svc == null)
            return Ok(new { error = $"Field introspection is not supported for datasource type '{ds.Type}'.", fields = Array.Empty<string>() });

        var (fields, error) = await svc.GetFieldsAsync(ds);
        if (fields.Count > 0) return Ok(fields);

        // Real-DB datasources (SQL Server / Power BI / Postgres / MySQL …):
        // NEVER serve a hardcoded placeholder list. Surface the real error so
        // the user can fix the connection rather than mapping charts to
        // columns that don't exist (which produced SQL like
        //   SELECT TOP 15 [Value], AVG([Value]) FROM [SalesLT].[Product]
        // failing with "Invalid column name").
        return Ok(new
        {
            error = error ?? "No fields were returned by the datasource. Verify the connection settings and that the user has SELECT/metadata permission.",
            fields = Array.Empty<string>()
        });
    }

    [HttpGet("{id}/tables")]
    public async Task<IActionResult> GetTables(int id)
    {
        var ds = await _db.Datasources.FindAsync(id);
        if (ds == null) return NotFound();

        var access = await EnsureViewAccessAsync(ds);
        if (access != null) return access;

        // Honour the user's curated allow-list. When the workspace wizard
        // saved `SelectedTables`, those are the tables the user explicitly
        // chose for this datasource — return them as-is so downstream UIs
        // (auto-report modal, properties panel, etc.) never see an empty
        // list just because a fresh introspection round-trip didn't fire.
        // Applies to SQL/PBI; REST/File services treat SelectedTables as
        // irrelevant since they expose a single virtual table.
        if (!string.IsNullOrWhiteSpace(ds.SelectedTables)
            && !QueryExecutionService.RestApiTypes.Contains(ds.Type ?? "")
            && !QueryExecutionService.FileUrlTypes.Contains(ds.Type ?? ""))
        {
            var selected = ds.SelectedTables
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => new
                {
                    name,
                    type = name.StartsWith("vw_", StringComparison.OrdinalIgnoreCase) ? "View" : "Table",
                    rowCount = 0
                })
                .ToList();
            if (selected.Count > 0) return Ok(selected);
        }

        var svc = ResolveTypeService(ds.Type);
        if (svc == null)
            return Ok(new { error = $"Datasource type '{ds.Type}' is not supported.", tables = Array.Empty<object>() });

        var (tables, error) = await svc.GetTablesAsync(ds);

        // REST / File services always return one virtual entry — surface as-is
        // so the UI shows the per-row error attached to that virtual table.
        if (tables.Count > 0)
        {
            var projected = tables.Select(t => t.Error == null
                ? (object)new { name = t.Name, type = t.Type, rowCount = t.RowCount }
                : new { name = t.Name, type = t.Type, rowCount = t.RowCount, error = t.Error }).ToList();
            return Ok(projected);
        }

        // Real-DB datasources (SQL Server / Power BI): never substitute a
        // SQL-Server-shaped placeholder list (Customers / Orders / Products …)
        // — that used to leak into AI prompts as if it were the real schema,
        // causing the auto-report generator to emit FROM clauses against
        // tables that do not exist. Return an empty list with the real
        // introspection error instead.
        return Ok(new
        {
            error = error ?? "No tables were returned by the datasource. Verify the connection settings and that the user has SELECT/metadata permission.",
            tables = Array.Empty<object>()
        });
    }

    [HttpGet("{id}/schema")]
    public async Task<IActionResult> GetSchema(int id)
    {
        var ds = await _db.Datasources.FindAsync(id);
        if (ds == null) return NotFound();

        var access = await EnsureViewAccessAsync(ds);
        if (access != null) return access;

        var svc = ResolveTypeService(ds.Type);
        if (svc == null)
        {
            return Ok(new
            {
                datasourceId = ds.Id,
                datasourceGuid = ds.Guid,
                datasourceName = ds.Name,
                datasourceType = ds.Type,
                error = $"Schema introspection is not supported for datasource type '{ds.Type}'.",
                tables = Array.Empty<object>()
            });
        }

        var (tables, error) = await svc.GetSchemaAsync(ds);

        if (tables.Count > 0)
        {
            var projected = tables.Select(t => (object)new
            {
                name = t.Name,
                type = t.Type,
                columns = t.Columns.Select(c => new { name = c.Name, dataType = c.DataType, isPrimaryKey = c.IsPrimaryKey }).ToList()
            }).ToList();

            return Ok(new
            {
                datasourceId = ds.Id,
                datasourceGuid = ds.Guid,
                datasourceName = ds.Name,
                datasourceType = ds.Type,
                tables = projected
            });
        }

        // Real-DB datasources (SQL Server / Power BI): NEVER substitute a
        // placeholder schema. Hardcoded generic columns (Id / Name / Value /
        // CreatedAt) used to leak into the Properties Panel field dropdowns
        // whenever introspection failed, and users would map charts to
        // columns that don't exist on their real tables — producing SQL like
        //     SELECT TOP 15 [Value], AVG([Value]) FROM [SalesLT].[Product] GROUP BY [Value]
        // which fails with "Invalid column name 'Value'". Surface the real
        // introspection error instead so the user can fix the connection.
        return Ok(new
        {
            datasourceId = ds.Id,
            datasourceGuid = ds.Guid,
            datasourceName = ds.Name,
            datasourceType = ds.Type,
            error = error ?? "No columns were returned by the datasource. Verify the connection settings and that the user has SELECT/metadata permission on the selected tables.",
            tables = Array.Empty<object>()
        });
    }

    [HttpPut("{guid}")]
    [RequireActiveSubscription]
    public async Task<IActionResult> Update(string guid, [FromBody] DatasourceRequest req)
    {
        var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null && int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? req.UserId ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        // Org sandbox: non-SuperAdmins cannot modify datasources from another organization
        if (appUser?.Role != "SuperAdmin" && callerOrgId > 0 && ds.OrganizationId != callerOrgId)
            return StatusCode(403, new { error = "You do not have access to this datasource." });

        var wsId = ds.WorkspaceId ?? req.WorkspaceId ?? 0;
        if (wsId > 0 && !await _permissions.CanEditAsync(wsId, userId))
            return StatusCode(403, new { error = "You need Editor or Admin role to update datasources." });

        if (req.Name != null) ds.Name = req.Name;
        if (req.ConnectionString != null) ds.ConnectionString = _encryption.Encrypt(req.ConnectionString);
        if (req.DbUser != null) ds.DbUser = _encryption.Encrypt(req.DbUser);
        if (req.DbPassword != null) ds.DbPassword = _encryption.Encrypt(req.DbPassword);
        if (req.XmlaEndpoint != null) ds.XmlaEndpoint = _encryption.Encrypt(req.XmlaEndpoint);
        if (req.MicrosoftAccountTenantId != null) ds.MicrosoftAccountTenantId = _encryption.Encrypt(req.MicrosoftAccountTenantId);
        if (req.SelectedTables != null) ds.SelectedTables = req.SelectedTables;
        if (req.ApiUrl != null) ds.ApiUrl = _encryption.Encrypt(req.ApiUrl);
        if (req.ApiKey != null) ds.ApiKey = _encryption.Encrypt(req.ApiKey);
        if (req.ApiMethod != null) ds.ApiMethod = req.ApiMethod;
        if (req.TransformEnabled.HasValue) ds.TransformEnabled = req.TransformEnabled.Value;
        if (req.TransformToml != null)
        {
            var parse = _transformService.Parse(req.TransformToml);
            if (!parse.Success)
                return BadRequest(new { error = parse.Error ?? "Invalid transform TOML." });
            ds.TransformToml = req.TransformToml;
        }

        await _db.SaveChangesAsync();
        // Flush any cached query results for this datasource — connection details or
        // selected tables may have changed, so stale results must not be served.
        _cacheInvalidator.InvalidateDatasource(ds.Id);
        return Ok(new { ds.Id, ds.Guid, ds.Name, ds.SelectedTables, ds.TransformEnabled, ds.TransformToml });
    }

    [HttpDelete("{guid}")]
    [RequireActiveSubscription]
    public async Task<IActionResult> Delete(string guid)
    {
        Datasource? ds = null;
        if (int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null)
            ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        var callerOrgId = appUser?.OrganizationId ?? 0;

        // Org sandbox: non-SuperAdmins cannot delete datasources from another organization
        if (appUser?.Role != "SuperAdmin" && callerOrgId > 0 && ds.OrganizationId != callerOrgId)
            return StatusCode(403, new { error = "You do not have access to this datasource." });

        var wsId = ds.WorkspaceId ?? 0;
        if (wsId > 0 && !await _permissions.CanDeleteAsync(wsId, userId))
            return StatusCode(403, new { error = "Only Admins can delete datasources." });

        // Null out Agent references to avoid FK constraint failures
        var agents = await _db.Agents.Where(a => a.DatasourceId == ds.Id).ToListAsync();
        foreach (var a in agents) a.DatasourceId = null;

        _db.Datasources.Remove(ds);
        await _db.SaveChangesAsync();
        _cacheInvalidator.InvalidateDatasource(ds.Id);
        return Ok(new { success = true });
    }

    [HttpGet("{guid}/transform")]
    public async Task<IActionResult> GetTransform(string guid)
    {
        var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null && int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null) return NotFound();

        var access = await EnsureViewAccessAsync(ds);
        if (access != null) return access;

        var parse = _transformService.Parse(ds.TransformToml);
        var draftList = await _db.TransformDrafts
            .Where(d => d.DatasourceId == ds.Id)
            .OrderByDescending(d => d.UpdatedAt)
            .Take(20)
            .Select(d => new
            {
                draftGuid = d.Guid,
                d.Name,
                d.Status,
                d.UpdatedAt,
                d.PublishedAt
            })
            .ToListAsync();

        var relatedDatasources = await _db.Datasources
            .Where(d => d.WorkspaceId == ds.WorkspaceId)
            .OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Guid, d.Name, d.Type })
            .ToListAsync();

        return Ok(new
        {
            datasourceId = ds.Id,
            datasourceGuid = ds.Guid,
            enabled = ds.TransformEnabled,
            toml = ds.TransformToml ?? "",
            parseSuccess = parse.Success,
            parseError = parse.Error,
            ruleCount = parse.Definition?.Rules.Count ?? 0,
            workbench = new
            {
                drafts = draftList,
                datasources = relatedDatasources,
                leftPanel = new { multiDatasource = true, aiAssist = true },
                centerPanel = new { pageSize = 1000, tableLike = true },
                rightPanel = new { steps = parse.Definition?.Rules.Count ?? 0 }
            }
        });
    }

    [HttpPost("{guid}/transform/draft")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTransformDraft(string guid, [FromBody] TransformDraftUpsertRequest? req)
    {
        var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null && int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null) return NotFound();

        var access = await EnsureEditAccessAsync(ds);
        if (access != null) return access;

        req ??= new TransformDraftUpsertRequest();
        var draft = await _transformDrafts.SaveDraftAsync(ds, req, User.FindFirstValue(ClaimTypes.NameIdentifier));
        return Ok(new
        {
            success = true,
            draftGuid = draft.Guid,
            draftStatus = draft.Status,
            draftUpdatedAt = draft.UpdatedAt
        });
    }

    [HttpGet("{guid}/transform/draft/{draftId}")]
    public async Task<IActionResult> LoadTransformDraft(string guid, string draftId)
    {
        var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null && int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null) return NotFound();

        var access = await EnsureViewAccessAsync(ds);
        if (access != null) return access;

        var draft = await _transformDrafts.LoadDraftAsync(ds.Id, draftId);
        if (draft == null) return NotFound();

        return Ok(new
        {
            success = true,
            draftGuid = draft.Guid,
            draft.Name,
            draft.Status,
            draft.TomlDefinition,
            draft.StepsJson,
            sourceAliases = draft.Sources.OrderBy(s => s.SortOrder).Select(s => s.Alias).ToList(),
            draft.UpdatedAt
        });
    }

    [HttpPost("{guid}/transform/validate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidateTransform(string guid, [FromBody] TransformValidateRequest? req)
    {
        var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null && int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null) return NotFound();

        var access = await EnsureViewAccessAsync(ds);
        if (access != null) return access;

        var validation = _transformValidation.ValidateToml(req?.Toml ?? ds.TransformToml);
        return Ok(new
        {
            success = validation.Success,
            syntaxErrors = validation.Issues.Select(i => new { code = i.Code, message = i.Message, reference = i.Reference }),
            stepCount = validation.ParsedSteps.Count,
            message = validation.Success ? "Validation passed." : "Validation failed.",
            aiSuggestion = validation.Suggestion
        });
    }

    [HttpPost("{guid}/transform/preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewTransform(string guid, [FromBody] TransformPreviewRequest? req)
    {
        var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null && int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null) return NotFound();

        var access = await EnsureViewAccessAsync(ds);
        if (access != null) return access;

        var rows = req?.Rows ?? new();
        if (rows.Count == 0 && (req?.AutoLoadDatasourceRows ?? true))
            rows = await LoadDatasourcePreviewRowsAsync(ds, req?.AutoLoadMaxRows ?? DefaultPreviewLoadRows);

        var sourceRows = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);
        if (req?.SourceRows?.Count > 0)
        {
            foreach (var kv in req.SourceRows)
                sourceRows[kv.Key] = kv.Value ?? new List<Dictionary<string, object>>();
        }
        if (req?.DatasourceGuids?.Count > 1)
        {
            var sourceDatasources = await _db.Datasources.Where(d => req.DatasourceGuids.Contains(d.Guid)).ToListAsync();
            foreach (var sds in sourceDatasources)
            {
                if (sourceRows.ContainsKey(sds.Guid)) continue;

                // Enforce isolation: every referenced datasource must belong to the
                // same workspace as the primary datasource AND the caller must have
                // view access on it. Without this, a user with rights to DS A could
                // include DS B (from another workspace/org) in DatasourceGuids and
                // the server would happily load and return its rows.
                if (sds.Id != ds.Id)
                {
                    if (sds.OrganizationId != ds.OrganizationId ||
                        sds.WorkspaceId != ds.WorkspaceId)
                    {
                        return StatusCode(403, new { error = $"Datasource '{sds.Name}' is not part of this workspace." });
                    }
                    var sAccess = await EnsureViewAccessAsync(sds);
                    if (sAccess != null) return sAccess;
                }

                sourceRows[sds.Guid] = await LoadDatasourcePreviewRowsAsync(sds, req.AutoLoadMaxRows ?? DefaultPreviewLoadRows);
            }
        }

        var previewDatasource = new Datasource
        {
            Id = ds.Id,
            Guid = ds.Guid,
            Name = ds.Name,
            Type = ds.Type,
            TransformEnabled = req?.Enabled ?? ds.TransformEnabled,
            TransformToml = req?.Toml ?? ds.TransformToml
        };

        var preview = _transformPreview.Preview(
            previewDatasource,
            req?.Toml ?? ds.TransformToml,
            rows,
            sourceRows.Count > 0 ? sourceRows : null,
            req?.Page ?? 1,
            req?.PageSize ?? TransformPreviewPageSize);

        if (req?.RecordAudit == true)
        {
            string messagesJson;
            try { messagesJson = JsonSerializer.Serialize(preview.Audit); }
            catch { messagesJson = "[]"; }

            _db.TransformRunAudits.Add(new TransformRunAudit
            {
                DatasourceId = ds.Id,
                Success = preview.Success,
                InputRowCount = rows.Count,
                OutputRowCount = preview.TotalRowCount,
                DurationMs = preview.RenderDurationMs,
                MessagesJson = messagesJson,
                Error = preview.Error,
                CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        return Ok(new
        {
            success = preview.Success,
            rowCount = preview.TotalRowCount,
            page = preview.Page,
            pageSize = preview.PageSize,
            totalPages = preview.TotalPages,
            data = preview.PagedRows,
            beforeRowCount = rows.Count,
            error = preview.Error,
            audit = preview.Audit,
            warnings = preview.Warnings,
            renderDurationMs = preview.RenderDurationMs,
            stepSummary = preview.StepSummary
        });
    }

    [HttpPost("{guid}/transform/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PublishTransform(string guid, [FromBody] TransformPublishRequest? req)
    {
        var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == guid);
        if (ds == null && int.TryParse(guid, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null) return NotFound();

        var access = await EnsureEditAccessAsync(ds);
        if (access != null) return access;

        var publish = await _transformPublish.PublishAsync(ds, req?.Toml ?? ds.TransformToml, User.FindFirstValue(ClaimTypes.NameIdentifier), req?.DraftGuid);
        if (!publish.Success)
            return BadRequest(new { success = false, error = publish.Error });

        _db.TransformRunAudits.Add(new TransformRunAudit
        {
            DatasourceId = ds.Id,
            Success = true,
            InputRowCount = 0,
            OutputRowCount = 0,
            DurationMs = 0,
            MessagesJson = "[]",
            Error = null,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            enabled = ds.TransformEnabled,
            toml = ds.TransformToml ?? "",
            publishTimestamp = DateTime.UtcNow
        });
    }

    [HttpPost("transform/workbench")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetMultiDatasourceWorkbench([FromBody] TransformWorkbenchRequest? req)
    {
        var guids = req?.DatasourceGuids?.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList() ?? new();
        if (guids.Count == 0) return BadRequest(new { error = "At least one datasource guid is required." });

        var datasources = await _db.Datasources.Where(d => guids.Contains(d.Guid)).ToListAsync();
        if (datasources.Count == 0) return NotFound();

        // Enforce that every referenced datasource lives in the same
        // organization AND workspace. The workbench is workspace-scoped — it
        // must not be possible to combine datasources from different
        // workspaces (or orgs) into one transform context, even if the
        // caller happens to have view access on both sides.
        var orgIds = datasources.Select(d => d.OrganizationId).Distinct().ToList();
        var wsIds = datasources.Select(d => d.WorkspaceId).Distinct().ToList();
        if (orgIds.Count > 1 || wsIds.Count > 1)
        {
            return StatusCode(403, new { error = "All datasources must belong to the same workspace." });
        }

        foreach (var ds in datasources)
        {
            var access = await EnsureViewAccessAsync(ds);
            if (access != null) return access;
        }

        var payload = new List<object>();
        var allColumns = new List<string>();
        foreach (var ds in datasources)
        {
            var rows = await LoadDatasourcePreviewRowsAsync(ds, TransformPreviewPageSize);
            var schema = rows.FirstOrDefault()?.Keys.ToList() ?? new List<string>();
            allColumns.AddRange(schema);
            payload.Add(new
            {
                ds.Id,
                ds.Guid,
                ds.Name,
                ds.Type,
                schema,
                sampleRowCount = rows.Count
            });
        }

        return Ok(new
        {
            success = true,
            datasources = payload,
            aiSuggestion = _transformSuggestion.SuggestNextStep(allColumns)
        });
    }

    private async Task<List<Dictionary<string, object>>> LoadDatasourcePreviewRowsAsync(Datasource ds, int maxRows)
    {
        maxRows = Math.Clamp(maxRows <= 0 ? DefaultPreviewLoadRows : maxRows, 1, MaxPreviewLoadRows);
        var svc = ResolveTypeService(ds.Type);
        if (svc == null) return new List<Dictionary<string, object>>();
        var (rows, _) = await svc.SamplePreviewRowsAsync(ds, maxRows);
        return rows.Count > 0 ? rows.ToList() : new List<Dictionary<string, object>>();
    }

    /// <summary>
    /// Workspace-scoped view-access guard shared by the introspection
    /// endpoints (fields / tables / schema). Returns a 403 result when the
    /// caller is not allowed to view the datasource, or null when access
    /// is granted.
    /// </summary>
    private async Task<IActionResult?> EnsureViewAccessAsync(Datasource ds)
    {
        if (ds.WorkspaceId.HasValue && ds.WorkspaceId.Value > 0)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (!await _permissions.CanViewAsync(ds.WorkspaceId.Value, userId))
            {
                var appUser = await _db.Users.FindAsync(userId);
                if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                    return StatusCode(403, new { error = "You do not have access to this datasource." });
            }
        }
        return null;
    }

    /// <summary>
    /// Edit-access guard for mutating operations (save draft, publish transform).
    /// Returns a 403 result when the caller does not have Editor or Admin role
    /// on the workspace, or null when access is granted.
    /// </summary>
    private async Task<IActionResult?> EnsureEditAccessAsync(Datasource ds)
    {
        if (ds.WorkspaceId.HasValue && ds.WorkspaceId.Value > 0)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (!await _permissions.CanEditAsync(ds.WorkspaceId.Value, userId))
            {
                var appUser = await _db.Users.FindAsync(userId);
                if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                    return StatusCode(403, new { error = "You need Editor or Admin role to modify transforms on this datasource." });
            }
        }
        return null;
    }

    private static string MaskConnectionString(string connStr)
    {
        if (string.IsNullOrEmpty(connStr)) return "";
        // Mask password values in the connection string
        var masked = System.Text.RegularExpressions.Regex.Replace(
            connStr,
            @"(Password|Pwd)\s*=\s*[^;]+",
            "$1=••••••",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return masked;
    }

    [HttpGet("{id}/relationships")]
    public async Task<IActionResult> GetRelationships(string id)
    {
        var ds = await _db.Datasources.FirstOrDefaultAsync(d => d.Guid == id);
        if (ds == null && int.TryParse(id, out var intId))
            ds = await _db.Datasources.FindAsync(intId);
        if (ds == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var appUser = await _db.Users.FindAsync(userId);
        if (appUser?.Role != "SuperAdmin" && appUser?.OrganizationId > 0 && ds.OrganizationId != appUser.OrganizationId)
            return StatusCode(403, new { error = "You do not have access to this datasource." });

        try
        {
            var rels = await _relationships.GetRelationshipsAsync(ds);
            return Ok(new
            {
                datasourceId = ds.Id,
                datasourceGuid = ds.Guid,
                relationships = rels.Select(r => new { r.FromTable, r.FromColumn, r.ToTable, r.ToColumn, r.Source, r.Confidence })
            });
        }
        catch (Exception ex)
        {
            return Ok(new { datasourceId = ds.Id, datasourceGuid = ds.Guid, relationships = Array.Empty<object>(), error = ex.Message });
        }
    }

    // ─── Phase 36 — Column Profiling (A11 stats + A9 quality) ─────────────────

    private static string QuoteIdent(string type, string name)
    {
        var safe = new string((name ?? "").Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == ' ').ToArray());
        if (QueryExecutionService.PowerBiTypes.Contains(type)) return "'" + safe.Replace("'", "''") + "'";
        if (string.Equals(type, "PostgreSQL", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Postgres", StringComparison.OrdinalIgnoreCase))
            return "\"" + safe + "\"";
        if (string.Equals(type, "MySQL", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "MariaDB", StringComparison.OrdinalIgnoreCase))
            return "`" + safe + "`";
        return "[" + safe + "]";
    }

    [HttpGet("{id}/profile")]
    public async Task<IActionResult> ProfileColumn(int id,
        [FromQuery] string table, [FromQuery] string column,
        [FromQuery] bool numeric = false, [FromQuery] int topN = 5)
    {
        var ds = await _db.Datasources.FindAsync(id);
        if (ds == null) return NotFound();
        if (ds.WorkspaceId.HasValue && ds.WorkspaceId.Value > 0)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (!await _permissions.CanViewAsync(ds.WorkspaceId.Value, userId))
            {
                var appUser = await _db.Users.FindAsync(userId);
                if (appUser?.Role != "OrgAdmin" && appUser?.Role != "SuperAdmin")
                    return StatusCode(403, new { error = "You do not have access to this datasource." });
            }
        }
        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(column))
            return BadRequest(new { error = "table and column are required." });
        if (QueryExecutionService.PowerBiTypes.Contains(ds.Type) || QueryExecutionService.RestApiTypes.Contains(ds.Type))
            return Ok(new { supported = false, reason = "Profiling is not supported for this datasource type." });

        var col = QuoteIdent(ds.Type, column);
        var tbl = QuoteIdent(ds.Type, table);
        var summarySql = numeric
            ? $"SELECT COUNT(*) AS total_count, COUNT({col}) AS non_null_count, COUNT(DISTINCT {col}) AS distinct_count, MIN({col}) AS min_val, MAX({col}) AS max_val, AVG(CAST({col} AS FLOAT)) AS avg_val FROM {tbl}"
            : $"SELECT COUNT(*) AS total_count, COUNT({col}) AS non_null_count, COUNT(DISTINCT {col}) AS distinct_count FROM {tbl}";
        var summary = await _queryService.ExecuteReadOnlyAsync(ds, summarySql, 1);
        if (!summary.Success || summary.Data.Count == 0)
            return Ok(new { supported = true, success = false, error = summary.Error ?? "No data returned." });
        var row = summary.Data[0];
        object? GetVal(string k) => row.TryGetValue(k, out var v) ? v : null;

        List<object>? topValues = null;
        try
        {
            var limit = Math.Max(1, Math.Min(20, topN));
            var isPgMy = string.Equals(ds.Type, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(ds.Type, "Postgres", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(ds.Type, "MySQL", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(ds.Type, "MariaDB", StringComparison.OrdinalIgnoreCase);
            string topSql = isPgMy
                ? $"SELECT {col} AS value, COUNT(*) AS freq FROM {tbl} WHERE {col} IS NOT NULL GROUP BY {col} ORDER BY COUNT(*) DESC LIMIT {limit}"
                : $"SELECT TOP {limit} {col} AS value, COUNT(*) AS freq FROM {tbl} WHERE {col} IS NOT NULL GROUP BY {col} ORDER BY COUNT(*) DESC";
            var topRes = await _queryService.ExecuteReadOnlyAsync(ds, topSql, limit);
            if (topRes.Success)
                topValues = topRes.Data.Select(r => (object)new { value = r.TryGetValue("value", out var v) ? v : null, count = r.TryGetValue("freq", out var c) ? c : 0 }).ToList();
        }
        catch { }

        return Ok(new
        {
            supported = true,
            success = true,
            datasourceId = ds.Id,
            table,
            column,
            numeric,
            rowCount = GetVal("total_count"),
            nonNullCount = GetVal("non_null_count"),
            distinctCount = GetVal("distinct_count"),
            min = GetVal("min_val"),
            max = GetVal("max_val"),
            avg = GetVal("avg_val"),
            topValues
        });
    }
}

public class DatasourceRequest
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? ConnectionString { get; set; }
    public string? DbUser { get; set; }
    public string? DbPassword { get; set; }
    public string? SelectedTables { get; set; }
    public string? XmlaEndpoint { get; set; }
    public string? MicrosoftAccountTenantId { get; set; }
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiMethod { get; set; }
    public bool? TransformEnabled { get; set; }
    public string? TransformToml { get; set; }
    public int OrganizationId { get; set; }
    public int? WorkspaceId { get; set; }
    public string? UserId { get; set; }
}

public class TransformPreviewRequest
{
    public bool? Enabled { get; set; }
    public string? Toml { get; set; }
    public List<Dictionary<string, object>> Rows { get; set; } = new();
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public bool? AutoLoadDatasourceRows { get; set; }
    public int? AutoLoadMaxRows { get; set; }
    public List<string>? DatasourceGuids { get; set; }
    public Dictionary<string, List<Dictionary<string, object>>>? SourceRows { get; set; }
    /// <summary>
    /// When true, writes a <see cref="TransformRunAudit"/> row for this preview run.
    /// Defaults to false to avoid unbounded audit table growth during iterative workbench use.
    /// </summary>
    public bool? RecordAudit { get; set; }
}

public class TransformValidateRequest
{
    public string? Toml { get; set; }
}

public class TransformPublishRequest
{
    public string? Toml { get; set; }
    public string? DraftGuid { get; set; }
}

public class TransformWorkbenchRequest
{
    public List<string>? DatasourceGuids { get; set; }
}
