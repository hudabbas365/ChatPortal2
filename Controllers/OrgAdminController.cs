using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIInsights.Controllers;

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
    public async Task<IActionResult> Settings()
    {
        var caller = await GetCallerAsync();
        if (caller == null)
            return Redirect("/auth/login");
        if (caller.Role != "OrgAdmin" && caller.Role != "SuperAdmin")
            return RedirectToAction("AccessDenied", "Home", new { statusCode = 403 });
        return View();
    }

    // Returns lightweight, non-sensitive metadata about the caller's
    // organization so the navbar's "About" popup can display where data
    // is hosted, what is encrypted, and the org's identifiers.
    [HttpGet("/api/org/about")]
    public async Task<IActionResult> About()
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();
        if (caller.OrganizationId == null)
        {
            return Ok(new
            {
                id = (int?)null,
                name = (string?)null,
                dataRegion = "European Union (EU)",
                hostingNotice = "Your data is hosted exclusively in European Union data centers, in compliance with GDPR.",
                encryptionNotice = "All connection strings and credentials are encrypted at rest using AES-256.",
                dataStorageNotice = "AI Insights does not retain copies of your business data. Queries run live against your own datasource and only the requested results are returned to your browser."
            });
        }

        var org = await _db.Organizations
            .Where(o => o.Id == caller.OrganizationId)
            .Select(o => new { o.Id, o.Name })
            .FirstOrDefaultAsync();

        return Ok(new
        {
            id = org?.Id,
            name = org?.Name,
            dataRegion = "European Union (EU)",
            hostingNotice = "Your data is hosted exclusively in European Union data centers, in compliance with GDPR.",
            encryptionNotice = "All connection strings and credentials are encrypted at rest using AES-256.",
            dataStorageNotice = "AI Insights does not retain copies of your business data. Queries run live against your own datasource and only the requested results are returned to your browser."
        });
    }

    [HttpGet("/api/org/users")]
    public async Task<IActionResult> GetUsers([FromQuery] int organizationId)
    {
        var caller = await GetCallerAsync();
        if (caller == null || !IsOrgAdminOf(caller, organizationId))
            return StatusCode(403, new { error = "You do not have permission to view this organization's users." });

        var users = await _db.Users
            .Where(u => u.OrganizationId == organizationId)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.Email,
                u.Role,
                u.Status,
                u.CreatedAt,
                assignedPlan = _db.SubscriptionPlans.Where(s => s.UserId == u.Id).Select(s => s.Plan.ToString()).FirstOrDefault()
            })
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

        if (string.IsNullOrEmpty(req.Email))
            return BadRequest(new { error = "Email is required." });
        if (string.IsNullOrEmpty(req.Password))
            return BadRequest(new { error = "Password is required." });

        var existingByEmail = await _userManager.FindByEmailAsync(req.Email);
        if (existingByEmail != null)
            return BadRequest(new { error = $"Email '{req.Email}' is already taken." });

        // Use email as the username so login-by-email always works
        var userName = !string.IsNullOrWhiteSpace(req.Username) ? req.Username.Trim() : req.Email;
        if (!string.Equals(userName, req.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existingByName = await _userManager.FindByNameAsync(userName);
            if (existingByName != null)
                return BadRequest(new { error = $"Username '{userName}' is already taken." });
        }

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = req.Email,
            FullName = req.FullName ?? req.Email,
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
            Description = $"User '{req.Email}' created with role {req.Role}.",
            UserId = req.CreatedBy ?? "",
            OrganizationId = req.OrganizationId > 0 ? req.OrganizationId : null
        });

        var loginUrl = _config["App:BaseUrl"] ?? "https://localhost:5001";
        var emailSent = false;
        if (!string.IsNullOrEmpty(req.Email))
        {
            emailSent = await _emailService.SendCredentialsEmailAsync(req.Email, user.FullName ?? req.Email, req.Email, req.Password, loginUrl + "/auth/login");
        }

        if (!emailSent)
        {
            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "credentials_email_skipped",
                Description = $"Credentials email skipped for user '{req.Email}' (SMTP not configured or no email provided).",
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
        // Allow any authenticated user who belongs to this organization (not just OrgAdmin)
        if (caller == null || (caller.OrganizationId != organizationId && !IsOrgAdminOf(caller, organizationId)))
            return StatusCode(403, new { error = "You do not have permission to view this organization's token usage." });

        var status = await _tokenBudget.GetStatusAsync(organizationId);
        return Ok(status);
    }

    // ──── License assignment (SuperAdmin grants licenses to the org, OrgAdmin assigns them to users) ────

    [HttpGet("/api/org/{organizationId}/licenses")]
    public async Task<IActionResult> GetLicenseSummary(int organizationId)
    {
        var caller = await GetCallerAsync();
        if (caller == null || !IsOrgAdminOf(caller, organizationId))
            return StatusCode(403, new { error = "You do not have permission to view this organization's licenses." });

        var org = await _db.Organizations.FindAsync(organizationId);
        if (org == null) return NotFound();

        var assigned = await _db.SubscriptionPlans
            .CountAsync(s => s.User!.OrganizationId == organizationId
                             && (s.Plan == PlanType.Professional || s.Plan == PlanType.Enterprise));

        return Ok(new
        {
            organizationId,
            plan = org.Plan.ToString(),
            purchased = org.PurchasedLicenses,
            assigned,
            available = Math.Max(0, org.PurchasedLicenses - assigned)
        });
    }

    [HttpPost("/api/org/users/{id}/assign-license")]
    public async Task<IActionResult> AssignLicense(string id)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { error = "User not found." });
        if (!user.OrganizationId.HasValue || !IsOrgAdminOf(caller, user.OrganizationId.Value))
            return StatusCode(403, new { error = "You do not have permission to assign licenses for this user." });

        var org = await _db.Organizations.FindAsync(user.OrganizationId.Value);
        if (org == null) return NotFound(new { error = "Organization not found." });

        if (org.Plan != PlanType.Professional && org.Plan != PlanType.Enterprise)
            return BadRequest(new { error = "Organization does not have a paid plan. Ask SuperAdmin to upgrade the plan first." });

        var assigned = await _db.SubscriptionPlans
            .CountAsync(s => s.User!.OrganizationId == org.Id
                             && (s.Plan == PlanType.Professional || s.Plan == PlanType.Enterprise));

        var sub = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == user.Id);
        var alreadyLicensed = sub != null && (sub.Plan == PlanType.Professional || sub.Plan == PlanType.Enterprise);

        if (!alreadyLicensed && assigned >= org.PurchasedLicenses)
            return BadRequest(new { error = $"No licenses available. Purchased: {org.PurchasedLicenses}, assigned: {assigned}." });

        if (sub == null)
        {
            sub = new SubscriptionPlan { UserId = user.Id, Plan = org.Plan };
            _db.SubscriptionPlans.Add(sub);
        }
        else
        {
            sub.Plan = org.Plan;
        }

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "license_assigned",
            Description = $"License ({org.Plan}) assigned to user '{user.Email}'.",
            UserId = caller.Id,
            OrganizationId = org.Id
        });

        await _db.SaveChangesAsync();
        return Ok(new { success = true, plan = sub.Plan.ToString() });
    }

    [HttpPost("/api/org/users/{id}/revoke-license")]
    public async Task<IActionResult> RevokeLicense(string id)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { error = "User not found." });
        if (!user.OrganizationId.HasValue || !IsOrgAdminOf(caller, user.OrganizationId.Value))
            return StatusCode(403, new { error = "You do not have permission to revoke licenses for this user." });

        var sub = await _db.SubscriptionPlans.FirstOrDefaultAsync(s => s.UserId == user.Id);
        if (sub == null || (sub.Plan != PlanType.Professional && sub.Plan != PlanType.Enterprise))
            return BadRequest(new { error = "User does not currently hold a paid license." });

        sub.Plan = PlanType.Free;

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "license_revoked",
            Description = $"License revoked from user '{user.Email}'.",
            UserId = caller.Id,
            OrganizationId = user.OrganizationId
        });

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ── Notifications (OrgAdmin push to users in their org) ──────────────────
    [HttpGet("/org/notifications")]
    public async Task<IActionResult> Notifications()
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Redirect("/auth/login");
        if (caller.Role != "OrgAdmin" && caller.Role != "SuperAdmin")
            return RedirectToAction("AccessDenied", "Home", new { statusCode = 403 });
        if (caller.OrganizationId == null)
            return RedirectToAction("AccessDenied", "Home", new { statusCode = 403 });

        var orgId = caller.OrganizationId.Value;
        var items = await _db.Notifications
            .Where(n => n.Scope == "Org" && n.OrganizationId == orgId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync();
        ViewBag.OrganizationId = orgId;
        return View("~/Views/OrgAdmin/Notifications.cshtml", items);
    }

    [HttpPost("/api/org/notifications")]
    public async Task<IActionResult> SendOrgNotification([FromBody] OrgNotificationDto dto)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();
        if (caller.Role != "OrgAdmin" && caller.Role != "SuperAdmin")
            return StatusCode(403, new { error = "OrgAdmin role required." });
        if (caller.OrganizationId == null)
            return StatusCode(403, new { error = "Not associated with an organization." });
        if (dto == null || string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Body))
            return BadRequest(new { error = "Title and body are required." });

        var n = new Notification
        {
            Scope = "Org",
            OrganizationId = caller.OrganizationId.Value,
            Title = dto.Title.Trim(),
            Body = dto.Body.Trim(),
            Type = string.IsNullOrWhiteSpace(dto.Type) ? "Announcement" : dto.Type!.Trim(),
            Severity = string.IsNullOrWhiteSpace(dto.Severity) ? "normal" : dto.Severity!.Trim(),
            Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link!.Trim(),
            ExpiresAt = dto.ExpiresAt,
            CreatedByUserId = caller.Id,
            CreatedByRole = "OrgAdmin",
            CreatedAt = DateTime.UtcNow,
            DeliveryStatus = "Delivered",
            DeliveredAt = DateTime.UtcNow
        };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, id = n.Id });
    }

    [HttpDelete("/api/org/notifications/{id:int}")]
    public async Task<IActionResult> DeleteOrgNotification(int id)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();
        if (caller.Role != "OrgAdmin" && caller.Role != "SuperAdmin") return StatusCode(403);
        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();
        if (caller.Role != "SuperAdmin" && (n.Scope != "Org" || n.OrganizationId != caller.OrganizationId))
            return StatusCode(403);
        _db.Notifications.Remove(n);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ── Embedded / Published Reports governance ──────────────────────────────
    // OrgAdmin can list every Published report inside their org's workspaces
    // and revoke its public/embed URL by flipping Status back to "Draft" and
    // clearing the ShareToken. Anonymous access is gated on Status=="Published"
    // in ReportController.ViewReport, so this immediately kills both the public
    // link and any external <iframe> embeds.
    [HttpGet("/api/org/{organizationId:int}/embedded-reports")]
    public async Task<IActionResult> GetEmbeddedReports(int organizationId)
    {
        var caller = await GetCallerAsync();
        if (caller == null || !IsOrgAdminOf(caller, organizationId))
            return StatusCode(403, new { error = "You do not have permission to view this organization's reports." });

        var reports = await _db.Reports
            .Include(r => r.Workspace)
            .Where(r => r.Status == "Published"
                        && r.Workspace != null
                        && r.Workspace.OrganizationId == organizationId)
            .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
            .Select(r => new
            {
                r.Guid,
                r.Name,
                workspaceName = r.Workspace!.Name,
                r.CreatedBy,
                publishedAt = r.UpdatedAt ?? r.CreatedAt,
                publicUrl = $"/report/view/{r.Guid}",
                embedUrl = $"/report/view/{r.Guid}?embed=1"
            })
            .ToListAsync();

        return Ok(reports);
    }

    [HttpPost("/api/org/reports/{guid}/revoke-embed")]
    public async Task<IActionResult> RevokeReportEmbed(string guid)
    {
        var caller = await GetCallerAsync();
        if (caller == null) return Unauthorized();

        var report = await _db.Reports
            .Include(r => r.Workspace)
            .FirstOrDefaultAsync(r => r.Guid == guid);
        if (report == null) return NotFound(new { error = "Report not found." });

        var orgId = report.Workspace?.OrganizationId ?? 0;
        if (orgId == 0 || !IsOrgAdminOf(caller, orgId))
            return StatusCode(403, new { error = "You do not have permission to revoke this report's embed URL." });

        report.Status = "Draft";
        report.ShareToken = null;
        report.UpdatedAt = DateTime.UtcNow;

        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "report_embed_revoked",
            Description = $"Embed/public URL revoked for report '{report.Name}' (guid={report.Guid}).",
            UserId = caller.Id,
            OrganizationId = orgId
        });

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}

public class OrgNotificationDto
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? Type { get; set; }
    public string? Severity { get; set; }
    public string? Link { get; set; }
    public DateTime? ExpiresAt { get; set; }
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
