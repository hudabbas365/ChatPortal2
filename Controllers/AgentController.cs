using ChatPortal2.Data;
using ChatPortal2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Controllers;

[Authorize]
[Route("api/agents")]
[ApiController]
public class AgentController : ControllerBase
{
    private readonly AppDbContext _db;

    public AgentController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? workspaceId, [FromQuery] int? organizationId)
    {
        var query = _db.Agents.AsQueryable();
        if (organizationId.HasValue)
            query = query.Where(a => a.OrganizationId == organizationId.Value);
        var agents = await query.ToListAsync();
        return Ok(agents);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AgentRequest req)
    {
        var agent = new Agent
        {
            Name = req.Name ?? "New Agent",
            SystemPrompt = req.SystemPrompt ?? "You are a helpful data assistant.",
            DatasourceId = req.DatasourceId,
            OrganizationId = req.OrganizationId
        };
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "agent_created",
            Description = $"Agent '{agent.Name}' created.",
            UserId = req.UserId ?? "",
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();

        return Ok(agent);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var agent = await _db.Agents.FindAsync(id);
        if (agent == null) return NotFound();
        _db.Agents.Remove(agent);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}

public class AgentRequest
{
    public string? Name { get; set; }
    public string? SystemPrompt { get; set; }
    public int? DatasourceId { get; set; }
    public int OrganizationId { get; set; }
    public string? UserId { get; set; }
}
