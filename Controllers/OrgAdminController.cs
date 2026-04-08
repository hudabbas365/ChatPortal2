using ChatPortal2.Data;
using ChatPortal2.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChatPortal2.Controllers;

public class OrgAdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public OrgAdminController(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet("/org/settings")]
    public IActionResult Settings() => View();

    [HttpGet("/api/org/users")]
    public async Task<IActionResult> GetUsers([FromQuery] int organizationId)
    {
        var users = await _db.Users
            .Where(u => u.OrganizationId == organizationId)
            .Select(u => new { u.Id, u.FullName, u.Email, u.Role, u.CreatedAt })
            .ToListAsync();
        return Ok(users);
    }

    [HttpPost("/api/org/users/invite")]
    public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest req)
    {
        if (string.IsNullOrEmpty(req.Email))
            return BadRequest(new { error = "Email is required." });

        var existing = await _userManager.FindByEmailAsync(req.Email);
        if (existing != null)
        {
            // If user exists, add to org
            existing.OrganizationId = req.OrganizationId;
            existing.Role = req.Role ?? "User";
            await _userManager.UpdateAsync(existing);
            return Ok(new { success = true, message = "User added to organization." });
        }

        // Create a new user with a temporary password
        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = req.Email,
            Role = req.Role ?? "User",
            OrganizationId = req.OrganizationId
        };
        // Generate a cryptographically random temporary password
        var randomBytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
        var tempPassword = "I!" + Convert.ToBase64String(randomBytes).Replace("+", "a").Replace("/", "b").Replace("=", "C")[..16];
        var result = await _userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
            return BadRequest(new { error = string.Join(", ", result.Errors.Select(e => e.Description)) });

        _db.SubscriptionPlans.Add(new SubscriptionPlan { UserId = user.Id, Plan = PlanType.Free });
        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "user_invited",
            Description = $"User {req.Email} invited with role {req.Role}.",
            UserId = req.InvitedBy ?? "",
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true, userId = user.Id, tempPassword });
    }

    [HttpPut("/api/org/users/{id}/role")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateRoleRequest req)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        user.Role = req.Role ?? user.Role;
        await _userManager.UpdateAsync(user);
        return Ok(new { success = true });
    }

    [HttpPut("/api/org/users/{id}/suspend")]
    public async Task<IActionResult> SuspendUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        return Ok(new { success = true });
    }

    [HttpDelete("/api/org/users/{id}")]
    public async Task<IActionResult> RemoveUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        user.OrganizationId = null;
        await _userManager.UpdateAsync(user);
        return Ok(new { success = true });
    }
}

public class InviteUserRequest
{
    public string? Email { get; set; }
    public string? Role { get; set; }
    public int OrganizationId { get; set; }
    public string? InvitedBy { get; set; }
}

public class UpdateRoleRequest
{
    public string? Role { get; set; }
}

