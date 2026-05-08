using System.Net;
using System.Net.Mail;
using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.SuperAdmin.Services;

public class OrganizationRetentionService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<OrganizationRetentionService> _logger;

    public OrganizationRetentionService(AppDbContext db, IConfiguration config, ILogger<OrganizationRetentionService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public DateTime GetPermanentDeleteAtUtc(DateTime deactivatedAtUtc) => deactivatedAtUtc.AddDays(90);

    public async Task<int> SendPrePermanentDeletionEmailAsync(
        Organization org,
        string initiatedByUserId,
        DateTime permanentDeleteAtUtc,
        bool immediateDeletion,
        CancellationToken ct = default)
    {
        var orgAdmins = await _db.Users
            .Where(u => u.OrganizationId == org.Id
                        && u.Role == "OrgAdmin"
                        && !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => new { u.Email, u.FullName })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var admin in orgAdmins)
        {
            if (string.IsNullOrWhiteSpace(admin.Email)) continue;
            var ok = await SendSmtpEmailAsync(
                admin.Email!,
                string.IsNullOrWhiteSpace(admin.FullName) ? admin.Email! : admin.FullName,
                org.Name,
                org.DeactivatedAt ?? DateTime.UtcNow,
                permanentDeleteAtUtc,
                immediateDeletion);

            if (ok) sent++;

            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "Org.PermanentDeleteEmailSent",
                Description = immediateDeletion
                    ? $"Permanent-deletion notice sent to OrgAdmin '{admin.Email}' for organization '{org.Name}' (Id={org.Id}); immediate deletion requested."
                    : $"Permanent-deletion notice sent to OrgAdmin '{admin.Email}' for organization '{org.Name}' (Id={org.Id}); scheduled deletion at {permanentDeleteAtUtc:O}.",
                UserId = initiatedByUserId,
                OrganizationId = org.Id,
                CreatedAt = DateTime.UtcNow
            });
        }

        return sent;
    }

    public async Task<(bool Success, string? Error)> PermanentlyDeleteOrganizationAsync(
        Organization org,
        string initiatedByUserId,
        string source,
        CancellationToken ct = default)
    {
        var id = org.Id;
        var orgName = org.Name;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var userIds = await _db.Users
                .Where(u => u.OrganizationId == id)
                .Select(u => u.Id)
                .ToListAsync(ct);

            var workspaceIds = await _db.Workspaces
                .Where(w => w.OrganizationId == id)
                .Select(w => w.Id)
                .ToListAsync(ct);

            var reportIds = workspaceIds.Count > 0
                ? await _db.Reports.Where(r => workspaceIds.Contains(r.WorkspaceId)).Select(r => r.Id).ToListAsync(ct)
                : new List<int>();

            await _db.ActivityLogs
                .Where(l => l.OrganizationId == id || (l.UserId != null && userIds.Contains(l.UserId)))
                .ExecuteDeleteAsync(ct);

            await _db.SupportTickets
                .Where(t => t.OrganizationId == id
                            || (t.UserId != null && userIds.Contains(t.UserId))
                            || (t.AssignedToUserId != null && userIds.Contains(t.AssignedToUserId)))
                .ExecuteDeleteAsync(ct);

            await _db.UserNotifications
                .Where(n => userIds.Contains(n.UserId))
                .ExecuteDeleteAsync(ct);

            await _db.Notifications
                .Where(n => n.OrganizationId == id
                            || (n.TargetUserId != null && userIds.Contains(n.TargetUserId)))
                .ExecuteDeleteAsync(ct);

            if (workspaceIds.Count > 0)
            {
                await _db.ChatMessages
                    .Where(m => workspaceIds.Contains(m.WorkspaceId))
                    .ExecuteDeleteAsync(ct);
            }
            if (userIds.Count > 0)
            {
                await _db.ChatMessages
                    .Where(m => userIds.Contains(m.UserId))
                    .ExecuteDeleteAsync(ct);
            }

            if (workspaceIds.Count > 0)
            {
                await _db.PinnedResults
                    .Where(p => workspaceIds.Contains(p.WorkspaceId))
                    .ExecuteDeleteAsync(ct);
            }
            if (userIds.Count > 0)
            {
                await _db.PinnedResults
                    .Where(p => userIds.Contains(p.UserId))
                    .ExecuteDeleteAsync(ct);
            }

            if (reportIds.Count > 0)
            {
                await _db.SharedReports
                    .Where(sr => reportIds.Contains(sr.ReportId))
                    .ExecuteDeleteAsync(ct);
                await _db.ReportRevisions
                    .Where(rr => reportIds.Contains(rr.ReportId))
                    .ExecuteDeleteAsync(ct);
            }

            if (userIds.Count > 0)
            {
                await _db.SubscriptionPlans
                    .Where(s => userIds.Contains(s.UserId))
                    .ExecuteDeleteAsync(ct);

                await _db.WorkspaceUsers
                    .Where(wu => userIds.Contains(wu.UserId))
                    .ExecuteDeleteAsync(ct);

                await _db.Users
                    .Where(u => userIds.Contains(u.Id))
                    .ExecuteDeleteAsync(ct);
            }

            _db.Organizations.Remove(org);
            await _db.SaveChangesAsync(ct);

            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "Org.PermanentDelete",
                Description = $"SuperAdmin permanent deletion completed for organization '{orgName}' (Id={id}). Source={source}.",
                UserId = initiatedByUserId,
                OrganizationId = null,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to permanently delete organization {OrgId}.", id);
            return (false, ex.Message);
        }
    }

    private async Task<bool> SendSmtpEmailAsync(
        string toEmail,
        string toName,
        string orgName,
        DateTime deactivatedAtUtc,
        DateTime permanentDeleteAtUtc,
        bool immediateDeletion)
    {
        var host = _config["Smtp:Host"];
        var port = int.TryParse(_config["Smtp:Port"], out var parsedPort) ? parsedPort : 587;
        var user = _config["Smtp:User"];
        var pass = _config["Smtp:Pass"];
        var from = _config["Smtp:From"] ?? user ?? "support@aiinsights365.net";
        var useSsl = !string.Equals(_config["Smtp:UseSsl"], "false", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning("SMTP host not configured. Skipping permanent-delete notice email.");
            return false;
        }

        var subject = immediateDeletion
            ? $"Organization '{orgName}' is being permanently deleted"
            : $"Organization '{orgName}' scheduled for permanent deletion";
        var html = BuildHtml(orgName, deactivatedAtUtc, permanentDeleteAtUtc, immediateDeletion);

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = useSsl,
                Credentials = !string.IsNullOrWhiteSpace(user)
                    ? new NetworkCredential(user, pass)
                    : null
            };

            using var msg = new MailMessage
            {
                From = new MailAddress(from),
                Subject = subject,
                Body = html,
                IsBodyHtml = true
            };
            msg.To.Add(string.IsNullOrWhiteSpace(toName) ? new MailAddress(toEmail) : new MailAddress(toEmail, toName));

            await client.SendMailAsync(msg);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send permanent-delete notice email.");
            return false;
        }
    }

    private static string BuildHtml(
        string orgName,
        DateTime deactivatedAtUtc,
        DateTime permanentDeleteAtUtc,
        bool immediateDeletion)
    {
        var safeOrg = WebUtility.HtmlEncode(orgName);
        var deactivatedAt = deactivatedAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'");
        var deleteAt = permanentDeleteAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'");
        var timingCopy = immediateDeletion
            ? "A Super Admin requested immediate permanent deletion. The organization is now being permanently deleted."
            : $"The organization will be permanently deleted on <strong>{deleteAt}</strong> unless restored before that time.";

        return $"""
            <div style='font-family:Inter,Arial,sans-serif;max-width:620px;margin:auto;color:#1f2937'>
              <h2 style='color:#991b1b'>Organization Deletion Notice</h2>
              <p>Hello,</p>
              <p>Your organization <strong>{safeOrg}</strong> is currently deactivated.</p>
              <p>Deactivated at: <strong>{deactivatedAt}</strong></p>
              <p>{timingCopy}</p>
              <p>If this organization should remain active, please contact your Super Admin or support immediately to request restoration.</p>
              <p style='margin-top:20px;color:#6b7280'>This is an automated lifecycle notice from AIInsights365.</p>
            </div>
            """;
    }
}
