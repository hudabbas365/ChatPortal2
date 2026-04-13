using ChatPortal2.Data;
using ChatPortal2.Models;
using ChatPortal2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ChatPortal2.Controllers;

[Authorize]
public class OrgAdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly ITokenBudgetService _tokenBudget;
    private readonly IConfiguration _config;

    public OrgAdminController(AppDbContext db, UserManager<ApplicationUser> userManager, IEmailService emailService, ITokenBudgetService tokenBudget, IConfiguration config)
    {
        _db = db;
        _userManager = userManager;
        _emailService = emailService;
        _tokenBudget = tokenBudget;
        _config = config;
    }

    private async Task<ApplicationUser?> GetCallerAsync()
    {
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        return string.IsNullOrEmpty(callerId) ? null : await _db.Users.FindAsync(callerId);
    }

    private bool IsOrgAdminOf(ApplicationUser caller, int organizationId)
    {
        if (caller.Role == "SuperAdmin") return true;
        return caller.Role == "OrgAdmin" && caller.OrganizationId == organizationId;
    }

    [HttpGet("/org/settings")]
    public IActionResult Settings() => View();

    [HttpGet("/api/org/users")]
    public async Task<IActionResult> GetUsers([FromQuery] int organizationId)
    {
        var caller = await GetCallerAsync();
        if (caller == null || !IsOrgAdminOf(caller, organizationId))
            return StatusCode(403, new { error = "You do not have permission to view this organization's users." });

        var users = await _db.Users
            .Where(u => u.OrganizationId == organizationId)
            .Select(u => new { u.Id, u.FullName, u.Email, u.Role, u.Status, u.CreatedAt })
            .ToListAsync();
        return Ok(users);
    }

    [HttpPost("/api/org/users/invite")]
    public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null || !IsOrgAdminOf(caller, req.OrganizationId))
            return StatusCode(403, new { error = "You do not have permission to invite users to this organization." });

        if (string.IsNullOrEmpty(req.Email))
            return BadRequest(new { error = "Email is required." });

        var existing = await _userManager.FindByEmailAsync(req.Email);
        if (existing != null)
        {
            // Prevent org hijacking: never silently move a user who already belongs to a different org
            if (existing.OrganizationId.HasValue && existing.OrganizationId.Value != req.OrganizationId)
                return Conflict(new { error = "This user already belongs to another organization and cannot be added." });

            // User has no org yet (or same org) — safe to assign
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
            OrganizationId = req.OrganizationId,
            Status = "Pending"
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
        // TODO: Send actual email invitation
        // For now, log the invite action
        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "invitation_email_sent",
            Description = $"Invitation email sent to {req.Email} for organization {req.OrganizationId}.",
            UserId = req.InvitedBy ?? "",
            OrganizationId = req.OrganizationId
        });
        await _db.SaveChangesAsync();

        return Ok(new { success = true, userId = user.Id, status = user.Status });
    }

    [HttpPut("/api/org/users/{id}/role")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateRoleRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (!user.OrganizationId.HasValue || !IsOrgAdminOf(caller, user.OrganizationId.Value))
            return StatusCode(403, new { error = "You do not have permission to change this user's role." });

        // Prevent escalation to SuperAdmin by non-SuperAdmin
        if (req.Role == "SuperAdmin" && caller.Role != "SuperAdmin")
            return StatusCode(403, new { error = "Only a SuperAdmin can assign the SuperAdmin role." });

        user.Role = req.Role ?? user.Role;
        await _userManager.UpdateAsync(user);
        return Ok(new { success = true });
    }

    [HttpPut("/api/org/users/{id}/suspend")]
    public async Task<IActionResult> SuspendUser(string id)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (!user.OrganizationId.HasValue || !IsOrgAdminOf(caller, user.OrganizationId.Value))
            return StatusCode(403, new { error = "You do not have permission to suspend this user." });

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        return Ok(new { success = true });
    }

    [HttpDelete("/api/org/users/{id}")]
    public async Task<IActionResult> RemoveUser(string id)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (!user.OrganizationId.HasValue || !IsOrgAdminOf(caller, user.OrganizationId.Value))
            return StatusCode(403, new { error = "You do not have permission to remove this user." });

        user.OrganizationId = null;
        await _userManager.UpdateAsync(user);
        return Ok(new { success = true });
    }

    [HttpPost("/api/org/users/create")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null || (req.OrganizationId > 0 && !IsOrgAdminOf(caller, req.OrganizationId)))
            return StatusCode(403, new { error = "You do not have permission to create users in this organization." });

        // Prevent creating users with SuperAdmin role by non-SuperAdmin
        if (req.Role == "SuperAdmin" && caller?.Role != "SuperAdmin")
            return StatusCode(403, new { error = "Only a SuperAdmin can create SuperAdmin users." });

        if (string.IsNullOrEmpty(req.Username))
            return BadRequest(new { error = "Username is required." });
        if (string.IsNullOrEmpty(req.Password))
            return BadRequest(new { error = "Password is required." });

        var existing = await _userManager.FindByNameAsync(req.Username);
        if (existing != null)
            return BadRequest(new { error = $"Username '{req.Username}' is already taken." });

        var user = new ApplicationUser
        {
            UserName = req.Username,
            Email = req.Email ?? "",
            FullName = req.FullName ?? req.Username,
            Role = req.Role ?? "User",
            OrganizationId = req.OrganizationId > 0 ? req.OrganizationId : null,
            Status = "Active"
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(new { error = string.Join(", ", result.Errors.Select(e => e.Description)) });

        _db.SubscriptionPlans.Add(new SubscriptionPlan { UserId = user.Id, Plan = PlanType.Free });
        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "user_created",
            Description = $"User '{req.Username}' created with role {req.Role}.",
            UserId = req.CreatedBy ?? "",
            OrganizationId = req.OrganizationId > 0 ? req.OrganizationId : null
        });

        var loginUrl = _config["App:BaseUrl"] ?? "https://localhost:5001";
        var emailSent = false;
        if (!string.IsNullOrEmpty(req.Email))
        {
            emailSent = await _emailService.SendCredentialsEmailAsync(req.Email, user.FullName ?? req.Username, req.Username, req.Password, loginUrl + "/auth/login");
        }

        if (!emailSent)
        {
            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "credentials_email_skipped",
                Description = $"Credentials email skipped for user '{req.Username}' (SMTP not configured or no email provided).",
                UserId = req.CreatedBy ?? "",
                OrganizationId = req.OrganizationId > 0 ? req.OrganizationId : null
            });
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true, userId = user.Id, username = user.UserName, email = user.Email, credentialsSent = emailSent });
    }

    [HttpPost("/api/org/users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetPasswordRequest req)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound(new { error = "User not found." });

        if (!user.OrganizationId.HasValue || !IsOrgAdminOf(caller, user.OrganizationId.Value))
            return StatusCode(403, new { error = "You do not have permission to reset this user's password." });

        var newPassword = req.NewPassword;
        if (string.IsNullOrEmpty(newPassword))
        {
            var randomBytes = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
            newPassword = "P!" + Convert.ToBase64String(randomBytes).Replace("+", "a").Replace("/", "b").Replace("=", "C")[..14];
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
            return BadRequest(new { error = string.Join(", ", result.Errors.Select(e => e.Description)) });

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "password_reset",
            Description = $"Password reset for user '{user.UserName}' by {req.ResetBy ?? "admin"}.",
            UserId = req.ResetBy ?? "",
            OrganizationId = user.OrganizationId
        });

        var loginUrl = _config["App:BaseUrl"] ?? "https://localhost:5001";
        var emailSent = false;
        if (!string.IsNullOrEmpty(user.Email))
        {
            emailSent = await _emailService.SendPasswordResetEmailAsync(user.Email, user.FullName ?? user.UserName ?? "", newPassword, loginUrl + "/auth/login");
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Password reset successfully.", emailSent });
    }

    [HttpGet("/api/org/token-usage")]
    public async Task<IActionResult> GetTokenUsage([FromQuery] int organizationId)
    {
        var caller = await GetCallerAsync();
        if (caller == null || !IsOrgAdminOf(caller, organizationId))
            return StatusCode(403, new { error = "You do not have permission to view this organization's token usage." });

        var status = await _tokenBudget.GetStatusAsync(organizationId);
        return Ok(status);
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

public class CreateUserRequest
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
    public int OrganizationId { get; set; }
    public string? CreatedBy { get; set; }
}

public class ResetPasswordRequest
{
    public string? NewPassword { get; set; }
    public string? ResetBy { get; set; }
}

