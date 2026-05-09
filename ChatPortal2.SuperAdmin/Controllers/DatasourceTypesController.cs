using AIInsights.Data;
using AIInsights.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace AIInsights.SuperAdmin.Controllers;

/// <summary>
/// Admin CRUD over the DB-backed datasource type registry. The same rows
/// drive the workspace wizard and the Transform Workbench "Add datasource"
/// modal, so any change here is live for end users on the next request — no
/// redeploy required.
/// </summary>
[Authorize]
public class DatasourceTypesController : Controller
{
    // Keep this whitelist in lock-step with DatasourceConnectionInfo so that
    // any value an admin saves can actually be bound to the Datasource entity
    // without a per-type code change.
    public static readonly string[] BindingKeys = new[]
    {
        "connectionString", "dbUser", "dbPassword",
        "xmlaEndpoint", "microsoftAccountTenantId",
        "apiUrl", "apiKey", "apiMethod"
    };

    public static readonly string[] RendererTypes = new[]
    {
        "text", "password", "url", "multiline", "select"
    };

    private readonly AppDbContext _db;

    public DatasourceTypesController(AppDbContext db) { _db = db; }

    private string? GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private async Task<bool> IsSuperAdminAsync()
    {
        if (!User.Claims.Any(c => c.Type == "role" && c.Value == "SuperAdmin"))
            return false;
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId)) return false;
        var user = await _db.Users.FindAsync(userId) as ApplicationUser;
        return user?.Role == "SuperAdmin";
    }

    // ──── GET /superadmin/datasource-types ────
    [HttpGet("/superadmin/datasource-types")]
    public async Task<IActionResult> Index()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var rows = await _db.DatasourceTypeDefinitions
            .AsNoTracking()
            .Include(d => d.Parameters)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.DisplayName)
            .ToListAsync();

        ViewData["Title"] = "Datasource Types";
        ViewData["ActivePage"] = "datasource-types";
        return View("~/Views/Admin/DatasourceTypes.cshtml", rows);
    }

    // ──── GET /superadmin/datasource-types/new ────
    [HttpGet("/superadmin/datasource-types/new")]
    public async Task<IActionResult> New()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        ViewData["Title"] = "New Datasource Type";
        ViewData["ActivePage"] = "datasource-types";
        ViewBag.BindingKeys = BindingKeys;
        ViewBag.RendererTypes = RendererTypes;
        return View("~/Views/Admin/DatasourceTypeEdit.cshtml", new DatasourceTypeDefinition
        {
            Enabled = true,
            SortOrder = 100
        });
    }

    // ──── GET /superadmin/datasource-types/{id}/edit ────
    [HttpGet("/superadmin/datasource-types/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var row = await _db.DatasourceTypeDefinitions
            .Include(d => d.Parameters.OrderBy(p => p.SortOrder))
            .FirstOrDefaultAsync(d => d.Id == id);
        if (row == null) return NotFound();

        ViewData["Title"] = "Edit Datasource Type";
        ViewData["ActivePage"] = "datasource-types";
        ViewBag.BindingKeys = BindingKeys;
        ViewBag.RendererTypes = RendererTypes;
        return View("~/Views/Admin/DatasourceTypeEdit.cshtml", row);
    }

    public class SaveRequest
    {
        public int? Id { get; set; }
        public string Type { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? Description { get; set; }
        public string? Icon { get; set; }
        public bool Enabled { get; set; } = true;
        public int SortOrder { get; set; }
        public bool RequiresGateway { get; set; }
        public string? GatewayHelp { get; set; }
        public List<SaveParam> Parameters { get; set; } = new();
    }
    public class SaveParam
    {
        public int? Id { get; set; }
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "text";
        public string? Placeholder { get; set; }
        public bool Required { get; set; } = true;
        public List<string>? Options { get; set; }
        public string? Help { get; set; }
        public int SortOrder { get; set; }
    }

    // ──── POST /api/superadmin/datasource-types ────
    [HttpPost("/api/superadmin/datasource-types")]
    public async Task<IActionResult> Save([FromBody] SaveRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (req == null) return BadRequest(new { error = "Empty body." });

        var type = (req.Type ?? "").Trim();
        if (string.IsNullOrEmpty(type)) return BadRequest(new { error = "Type is required." });
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return BadRequest(new { error = "Display name is required." });

        // Reject params that don't bind to the Datasource entity. This is
        // critical: any other key would silently drop the user's input on the
        // server — better to fail the save loudly so the admin can fix it.
        foreach (var p in req.Parameters ?? new())
        {
            if (string.IsNullOrWhiteSpace(p.Key))
                return BadRequest(new { error = "Each parameter needs a Key." });
            if (!BindingKeys.Contains(p.Key))
                return BadRequest(new { error = $"Unknown binding key '{p.Key}'. Allowed: {string.Join(", ", BindingKeys)}." });
            if (!RendererTypes.Contains(p.Type))
                return BadRequest(new { error = $"Unknown renderer type '{p.Type}'. Allowed: {string.Join(", ", RendererTypes)}." });
        }

        var dupKeys = (req.Parameters ?? new())
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupKeys.Count > 0)
            return BadRequest(new { error = "Duplicate parameter keys: " + string.Join(", ", dupKeys) });

        var callerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        var now = DateTime.UtcNow;

        DatasourceTypeDefinition def;
        if (req.Id.HasValue && req.Id.Value > 0)
        {
            def = await _db.DatasourceTypeDefinitions
                .Include(d => d.Parameters)
                .FirstOrDefaultAsync(d => d.Id == req.Id.Value)
                ?? throw new InvalidOperationException("Definition not found.");
            // Locked fields for built-in rows: Type cannot change (would
            // detach it from its IDatasourceTypeService implementation).
            if (!def.IsBuiltIn) def.Type = type;
        }
        else
        {
            // Reject duplicate Type names early — DB has a unique index but
            // a friendly error is nicer than a 500.
            if (await _db.DatasourceTypeDefinitions.AnyAsync(d => d.Type == type))
                return BadRequest(new { error = $"A datasource type named '{type}' already exists." });
            def = new DatasourceTypeDefinition { Type = type, IsBuiltIn = false, CreatedUtc = now };
            _db.DatasourceTypeDefinitions.Add(def);
        }

        def.DisplayName     = req.DisplayName.Trim();
        def.Description     = req.Description;
        def.Icon            = string.IsNullOrWhiteSpace(req.Icon) ? null : req.Icon.Trim();
        def.Enabled         = req.Enabled;
        def.SortOrder       = req.SortOrder;
        def.RequiresGateway = req.RequiresGateway;
        def.GatewayHelp     = req.GatewayHelp;
        def.UpdatedUtc      = now;
        def.UpdatedBy       = callerEmail;

        // Reconcile parameters: drop those no longer present, update existing
        // by Id, insert new rows.
        var keepIds = new HashSet<int>((req.Parameters ?? new()).Where(p => p.Id.HasValue).Select(p => p.Id!.Value));
        foreach (var existing in def.Parameters.ToList())
        {
            if (!keepIds.Contains(existing.Id)) _db.Remove(existing);
        }
        var byId = def.Parameters.ToDictionary(p => p.Id);
        foreach (var p in req.Parameters ?? new())
        {
            DatasourceTypeParameter row;
            if (p.Id.HasValue && byId.TryGetValue(p.Id.Value, out var ex)) row = ex;
            else
            {
                row = new DatasourceTypeParameter();
                def.Parameters.Add(row);
            }
            row.Key         = p.Key.Trim();
            row.Label       = string.IsNullOrWhiteSpace(p.Label) ? p.Key : p.Label.Trim();
            row.Type        = p.Type;
            row.Placeholder = p.Placeholder;
            row.Required    = p.Required;
            row.Help        = p.Help;
            row.SortOrder   = p.SortOrder;
            row.OptionsJson = (p.Options != null && p.Options.Count > 0)
                ? JsonSerializer.Serialize(p.Options)
                : null;
        }

        await _db.SaveChangesAsync();

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "DatasourceType.Save",
            Description = $"Saved datasource type '{def.Type}' ({def.Parameters.Count} parameter(s)).",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = now
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true, id = def.Id });
    }

    // ──── POST /api/superadmin/datasource-types/{id}/toggle ────
    [HttpPost("/api/superadmin/datasource-types/{id:int}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var def = await _db.DatasourceTypeDefinitions.FindAsync(id);
        if (def == null) return NotFound();
        def.Enabled = !def.Enabled;
        def.UpdatedUtc = DateTime.UtcNow;
        def.UpdatedBy = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        await _db.SaveChangesAsync();
        return Ok(new { success = true, enabled = def.Enabled });
    }

    // ──── DELETE /api/superadmin/datasource-types/{id} ────
    [HttpDelete("/api/superadmin/datasource-types/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var def = await _db.DatasourceTypeDefinitions
            .Include(d => d.Parameters)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (def == null) return NotFound();
        if (def.IsBuiltIn)
            return BadRequest(new { error = "Built-in datasource types cannot be deleted. Disable instead." });
        _db.RemoveRange(def.Parameters);
        _db.Remove(def);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}
