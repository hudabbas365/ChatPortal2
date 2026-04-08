using ChatPortal2.Data;
using ChatPortal2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Controllers;

[Authorize]
public class WorkspaceController : Controller
{
    private readonly AppDbContext _db;

    public WorkspaceController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("/api/workspaces")]
    public async Task<IActionResult> GetAll([FromQuery] int organizationId)
    {
        var workspaces = await _db.Workspaces
            .Where(w => w.OrganizationId == organizationId)
            .ToListAsync();
        return Ok(workspaces);
    }

    [HttpPost("/api/workspaces")]
    public async Task<IActionResult> Create([FromBody] WorkspaceRequest req)
    {
        var workspace = new Workspace
        {
            Name = req.Name ?? "New Workspace",
            OrganizationId = req.OrganizationId
        };
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync();

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "workspace_created",
            Description = $"Workspace '{workspace.Name}' created.",
            UserId = req.UserId ?? "",
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();

        return Ok(workspace);
    }

    [HttpDelete("/api/workspaces/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var workspace = await _db.Workspaces.FindAsync(id);
        if (workspace == null) return NotFound();
        _db.Workspaces.Remove(workspace);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}

public class WorkspaceRequest
{
    public string? Name { get; set; }
    public int OrganizationId { get; set; }
    public string? UserId { get; set; }
}
