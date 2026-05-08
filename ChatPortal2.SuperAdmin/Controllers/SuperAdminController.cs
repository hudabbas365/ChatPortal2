using AIInsights.Data;
using AIInsights.Models;
using AIInsights.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIInsights.SuperAdmin.Controllers;

[Authorize]
public class SuperAdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly CohereService _cohere;
    private readonly IServiceScopeFactory _scopeFactory;

    public SuperAdminController(AppDbContext db, CohereService cohere, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _cohere = cohere;
        _scopeFactory = scopeFactory;
    }

    protected string? GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    // Verifies SuperAdmin role both from JWT claims AND database for defense-in-depth
    protected async Task<bool> IsSuperAdminAsync()
    {
        if (!User.Claims.Any(c => c.Type == "role" && c.Value == "SuperAdmin"))
            return false;
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId)) return false;
        var user = await _db.Users.FindAsync(userId) as ApplicationUser;
        return user?.Role == "SuperAdmin";
    }

    [HttpGet("/superadmin")]
    public async Task<IActionResult> Index()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var stats = await GetDashboardStatsAsync();
        ViewBag.TotalOrgs = stats.TotalOrgs;
        ViewBag.TotalUsers = stats.TotalUsers;
        ViewBag.TotalWorkspaces = stats.TotalWorkspaces;
        ViewBag.TotalMessages = stats.TotalMessages;
        ViewBag.ProUsers = stats.ProSubscriptions;
        ViewBag.EnterpriseUsers = stats.EnterpriseSubscriptions;
        ViewBag.TotalIncome = stats.TotalIncome;
        ViewBag.ActiveTrials = stats.ActiveTrials;
        ViewBag.ActiveNow = stats.ActiveNow;
        ViewBag.ActiveToday = stats.ActiveToday;
        ViewBag.Dau = stats.Dau;
        ViewBag.Wau = stats.Wau;
        ViewBag.Mau = stats.Mau;

        // Recent organizations list — surfaces the OrganizationGuid on the dashboard so
        // SuperAdmin can copy it without having to navigate into the Organizations page.
        ViewBag.RecentOrganizations = await _db.Organizations
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Select(o => new OrgGuidRow
            {
                Id = o.Id,
                Name = o.Name,
                OrganizationGuid = o.OrganizationGuid,
                CreatedAt = o.CreatedAt
            })
            .Take(10)
            .ToListAsync();

        return View("~/Views/Admin/Index.cshtml");
    }

    public class OrgGuidRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Guid OrganizationGuid { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    [HttpGet("/api/superadmin/dashboard-stats")]
    public async Task<IActionResult> DashboardStats()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var stats = await GetDashboardStatsAsync();
        return Ok(stats);
    }

    private async Task<DashboardStatsDto> GetDashboardStatsAsync()
    {
        var totalOrgs = await _db.Organizations.CountAsync();
        var totalUsers = await _db.Users.CountAsync();
        var totalWorkspaces = await _db.Workspaces.CountAsync();
        var totalMessages = await _db.ChatMessages.CountAsync();

        var proCount = await _db.SubscriptionPlans.CountAsync(p => p.Plan == PlanType.Professional);
        var enterpriseCount = await _db.SubscriptionPlans.CountAsync(p => p.Plan == PlanType.Enterprise);
        var nowUtc = DateTime.UtcNow;
        var activeTrials = await _db.SubscriptionPlans
            .CountAsync(p => p.Plan == PlanType.FreeTrial && p.TrialEndDate != null && p.TrialEndDate >= nowUtc);

        // Actual revenue collected this calendar month from succeeded payments,
        // matching the Revenue page (Payments action). The previous calculation
        // (proCount * price + enterpriseCount * price) returned a theoretical
        // MRR that ignored cancellations, refunds, and real collections.
        var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthlyRevenue = await _db.PaymentRecords
            .Where(p => p.Status == "succeeded" && p.CreatedAt >= monthStart)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;

        var now = DateTime.UtcNow;
        var activeNow = await _db.Users.CountAsync(u => u.LastSeenAt != null && u.LastSeenAt >= now.AddMinutes(-5));
        var activeToday = await _db.Users.CountAsync(u => u.LastSeenAt != null && u.LastSeenAt >= now.Date);

        var mauCutoff = now.AddDays(-30);
        var wauCutoff = now.AddDays(-7);
        var dauCutoff = now.AddDays(-1);

        var activityCounts = await _db.ActivityLogs
            .Where(l => l.CreatedAt >= mauCutoff)
            .GroupBy(l => l.UserId)
            .Select(g => new { UserId = g.Key, MaxDate = g.Max(l => l.CreatedAt) })
            .ToListAsync();

        var mau = activityCounts.Count;
        var wau = activityCounts.Count(g => g.MaxDate >= wauCutoff);
        var dau = activityCounts.Count(g => g.MaxDate >= dauCutoff);

        return new DashboardStatsDto
        {
            TotalOrgs = totalOrgs,
            TotalUsers = totalUsers,
            TotalWorkspaces = totalWorkspaces,
            TotalMessages = totalMessages,
            ProSubscriptions = proCount,
            EnterpriseSubscriptions = enterpriseCount,
            TotalIncome = monthlyRevenue,
            ActiveTrials = activeTrials,
            ActiveNow = activeNow,
            ActiveToday = activeToday,
            Dau = dau,
            Wau = wau,
            Mau = mau
        };
    }

    public class DashboardStatsDto
    {
        public int TotalOrgs { get; set; }
        public int TotalUsers { get; set; }
        public int TotalWorkspaces { get; set; }
        public int TotalMessages { get; set; }
        [Newtonsoft.Json.JsonProperty("proUsers")]
        public int ProSubscriptions { get; set; }
        [Newtonsoft.Json.JsonProperty("enterpriseUsers")]
        public int EnterpriseSubscriptions { get; set; }
        public decimal TotalIncome { get; set; }
        public int ActiveTrials { get; set; }
        public int ActiveNow { get; set; }
        public int ActiveToday { get; set; }
        public int Dau { get; set; }
        public int Wau { get; set; }
        public int Mau { get; set; }
    }

    [HttpGet("/superadmin/organizations")]
    public async Task<IActionResult> Organizations()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        // Use split query to avoid cartesian explosion from multiple collection includes,
        // which in EF Core 8 can cause some parent rows to be dropped when duplicate
        // ordering-key values combine with the large cross-product.
        var orgs = await _db.Organizations
            .Include(o => o.Users)
                .ThenInclude(u => u.Subscription)
            .Include(o => o.Workspaces)
            .AsSplitQuery()
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .ToListAsync();
        return View("~/Views/Admin/Organizations.cshtml", orgs);
    }

    [HttpGet("/superadmin/organizations/trash")]
    public async Task<IActionResult> OrganizationsTrash()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var now = DateTime.UtcNow;
        var archives = await _db.OrganizationDeletionArchives
            .OrderByDescending(a => a.DeletedAt)
            .Select(a => new OrganizationTrashRowViewModel
            {
                ArchiveId = a.Id,
                Name = a.Name,
                OriginalOrganizationGuid = a.OriginalOrganizationGuid,
                DeletedAt = a.DeletedAt,
                DeletedByDisplayName = a.DeletedByDisplayName,
                DeletedByUserId = a.DeletedByUserId,
                ExpiresAt = a.ExpiresAt,
                RestoredAt = a.RestoredAt,
                SizeBytes = a.SizeBytes,
                IsExpired = a.ExpiresAt <= now
            })
            .ToListAsync();

        return View("~/Views/Admin/OrganizationsTrash.cshtml", archives);
    }

    public class OrganizationTrashRowViewModel
    {
        public int ArchiveId { get; set; }
        public string Name { get; set; } = "";
        public Guid OriginalOrganizationGuid { get; set; }
        public DateTime DeletedAt { get; set; }
        public string? DeletedByDisplayName { get; set; }
        public string? DeletedByUserId { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RestoredAt { get; set; }
        public int SizeBytes { get; set; }
        public bool IsExpired { get; set; }
    }

    [HttpGet("/api/admin/super/orgs")]
    public async Task<IActionResult> GetOrganizationsForPlanEditor()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var orgs = await _db.Organizations
            .OrderBy(o => o.Name)
            .Select(o => new { o.Id, o.Name, plan = o.Plan.ToString() })
            .ToListAsync();
        return Ok(orgs);
    }

    [HttpPut("/api/admin/super/orgs/{id}/plan")]
    public async Task<IActionResult> UpdateOrgPlan(int id, [FromBody] UpdateOrgPlanRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();
        if (!Enum.TryParse<PlanType>(req.Plan, true, out var plan))
            return BadRequest(new { error = "Invalid plan. Use: Free, FreeTrial, Professional, Enterprise" });

        org.Plan = plan;

        // SuperAdmin grants a number of paid licenses to the organization.
        // OrgAdmin will assign these licenses to individual users.
        if (req.PurchasedLicenses.HasValue)
        {
            if (req.PurchasedLicenses.Value < 0)
                return BadRequest(new { error = "PurchasedLicenses must be zero or greater." });

            // Never let PurchasedLicenses drop below the number already assigned to users.
            var assignedCount = await _db.SubscriptionPlans
                .CountAsync(s => s.User!.OrganizationId == id
                                 && (s.Plan == PlanType.Professional || s.Plan == PlanType.Enterprise));

            if (req.PurchasedLicenses.Value < assignedCount)
                return BadRequest(new { error = $"Cannot reduce licenses below the {assignedCount} already assigned. Revoke user licenses first." });

            // Free / FreeTrial plans don't carry paid licenses.
            org.PurchasedLicenses = (plan == PlanType.Professional || plan == PlanType.Enterprise)
                ? req.PurchasedLicenses.Value
                : 0;
        }
        else if (plan != PlanType.Professional && plan != PlanType.Enterprise)
        {
            // Downgrading to a non-paid plan clears the license pool.
            org.PurchasedLicenses = 0;
        }

        await _db.SaveChangesAsync();
        return Ok(new
        {
            success = true,
            orgId = id,
            plan = org.Plan.ToString(),
            purchasedLicenses = org.PurchasedLicenses
        });
    }

    /// <summary>
    /// Permanently deletes an organization and all of its related data:
    /// users, subscriptions, workspaces, agents, datasources, dashboards,
    /// reports, chat messages, pinned results, token usage, payment records,
    /// activity logs, notifications, support tickets, etc.
    /// Many relationships already cascade via the model configuration; this
    /// method explicitly cleans up the rest inside a single transaction.
    /// </summary>
    [HttpDelete("/api/admin/super/orgs/{id}")]
    public async Task<IActionResult> DeleteOrganization(int id, [FromQuery] string? confirm, [FromQuery] Guid? orgGuid = null)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        if (orgGuid.HasValue && orgGuid.Value != org.OrganizationGuid)
            return BadRequest(new { error = "Organization GUID does not match the selected organization." });

        var duplicateNameCount = await _db.Organizations.CountAsync(o => o.Name == org.Name);
        var expectedConfirm = duplicateNameCount > 1
            ? BuildDisambiguationToken(org.Name, org.OrganizationGuid)
            : org.Name;

        if (string.IsNullOrWhiteSpace(confirm) ||
            !string.Equals(confirm.Trim(), expectedConfirm, StringComparison.Ordinal))
        {
            var message = duplicateNameCount > 1
                ? "Confirmation text does not match the required disambiguation token."
                : "Confirmation text does not match the organization name.";
            return BadRequest(new { error = message });
        }

        var actorId = GetCurrentUserId();
        var actorDisplayName = !string.IsNullOrWhiteSpace(actorId)
            ? await _db.Users.Where(u => u.Id == actorId).Select(u => u.FullName).FirstOrDefaultAsync()
            : null;
        var orgName = org.Name;

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // ── Collect dependent IDs up-front
            var userIds = await _db.Users
                .Where(u => u.OrganizationId == id)
                .Select(u => u.Id)
                .ToListAsync();

            var workspaceIds = await _db.Workspaces
                .Where(w => w.OrganizationId == id)
                .Select(w => w.Id)
                .ToListAsync();

            var reportIds = workspaceIds.Count > 0
                ? await _db.Reports.Where(r => workspaceIds.Contains(r.WorkspaceId)).Select(r => r.Id).ToListAsync()
                : new List<int>();

            // Archive snapshot before any destructive delete.
            try
            {
                var snapshot = await BuildDeletionSnapshotAsync(id, userIds, workspaceIds, reportIds);
                var snapshotJson = JsonSerializer.Serialize(snapshot, OrganizationArchiveJsonOptions);
                var deletedAt = DateTime.UtcNow;
                var snapshotSizeBytes = Encoding.UTF8.GetByteCount(snapshotJson);

                _db.OrganizationDeletionArchives.Add(new OrganizationDeletionArchive
                {
                    OriginalOrganizationId = org.Id,
                    OriginalOrganizationGuid = org.OrganizationGuid,
                    Name = org.Name,
                    DeletedAt = deletedAt,
                    DeletedByUserId = actorId,
                    DeletedByDisplayName = actorDisplayName,
                    ExpiresAt = deletedAt.AddDays(30),
                    Snapshot = snapshotJson,
                    SizeBytes = snapshotSizeBytes
                });
                await _db.SaveChangesAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { error = "Failed to archive organization before deletion." });
            }

            // ── Tables that don't cascade — clean them explicitly.
            // ActivityLog (Org or User)
            await _db.ActivityLogs
                .Where(l => l.OrganizationId == id || (l.UserId != null && userIds.Contains(l.UserId)))
                .ExecuteDeleteAsync();

            // SupportTickets
            await _db.SupportTickets
                .Where(t => t.OrganizationId == id
                            || (t.UserId != null && userIds.Contains(t.UserId))
                            || (t.AssignedToUserId != null && userIds.Contains(t.AssignedToUserId)))
                .ExecuteDeleteAsync();

            // Notifications + UserNotifications
            await _db.UserNotifications
                .Where(n => userIds.Contains(n.UserId))
                .ExecuteDeleteAsync();

            await _db.Notifications
                .Where(n => n.OrganizationId == id
                            || (n.TargetUserId != null && userIds.Contains(n.TargetUserId)))
                .ExecuteDeleteAsync();

            // ChatMessages (per workspace and per user)
            if (workspaceIds.Count > 0)
            {
                await _db.ChatMessages
                    .Where(m => workspaceIds.Contains(m.WorkspaceId))
                    .ExecuteDeleteAsync();
            }
            if (userIds.Count > 0)
            {
                await _db.ChatMessages
                    .Where(m => userIds.Contains(m.UserId))
                    .ExecuteDeleteAsync();
            }

            // PinnedResults (per workspace and per user)
            if (workspaceIds.Count > 0)
            {
                await _db.PinnedResults
                    .Where(p => workspaceIds.Contains(p.WorkspaceId))
                    .ExecuteDeleteAsync();
            }
            if (userIds.Count > 0)
            {
                await _db.PinnedResults
                    .Where(p => userIds.Contains(p.UserId))
                    .ExecuteDeleteAsync();
            }

            // SharedReports for the org's reports (cascade also handles this, but
            // we delete here defensively before report rows go away).
            if (reportIds.Count > 0)
            {
                await _db.SharedReports
                    .Where(sr => reportIds.Contains(sr.ReportId))
                    .ExecuteDeleteAsync();
                await _db.ReportRevisions
                    .Where(rr => reportIds.Contains(rr.ReportId))
                    .ExecuteDeleteAsync();
            }

            // Identity-related: detach users from org so cascade-on-delete on the
            // org row doesn't try to SetNull-then-Cascade across competing paths,
            // and so we can hard-delete the user rows ourselves.
            if (userIds.Count > 0)
            {
                // SubscriptionPlans cascade with User, but delete explicitly to
                // avoid SQL Server multiple-cascade-path errors on some schemas.
                await _db.SubscriptionPlans
                    .Where(s => userIds.Contains(s.UserId))
                    .ExecuteDeleteAsync();

                await _db.WorkspaceUsers
                    .Where(wu => userIds.Contains(wu.UserId))
                    .ExecuteDeleteAsync();

                // Identity user rows
                await _db.Users
                    .Where(u => userIds.Contains(u.Id))
                    .ExecuteDeleteAsync();
            }

            // Finally remove the organization itself. Remaining dependents
            // (Workspaces, Agents, Datasources, TokenUsages, PaymentRecords,
            // PlanChangeLogs, Dashboards/Reports under workspaces, etc.) are
            // wired with cascade in OnModelCreating and will be removed by SQL.
            _db.Organizations.Remove(org);
            await _db.SaveChangesAsync();

            // Audit trail (after the org row is gone — log carries no FK on Org).
            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "OrganizationDeleted",
                Description = $"SuperAdmin deleted organization '{orgName}' (Id={id}) and all related data.",
                UserId = actorId ?? "",
                OrganizationId = null,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            await tx.CommitAsync();
            return Ok(new { success = true, deletedOrgId = id, name = orgName });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { error = "Failed to delete organization.", detail = ex.Message });
        }
    }

    [HttpPost("/api/admin/super/orgs/trash/{archiveId}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreOrganizationArchive(int archiveId)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var archive = await _db.OrganizationDeletionArchives.FirstOrDefaultAsync(a => a.Id == archiveId);
        if (archive == null) return NotFound();
        if (archive.RestoredAt != null) return BadRequest(new { error = "Archive has already been restored." });
        if (archive.ExpiresAt <= DateTime.UtcNow) return BadRequest(new { error = "Archive has expired and can no longer be restored." });

        OrganizationDeletionSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<OrganizationDeletionSnapshot>(archive.Snapshot, OrganizationArchiveJsonOptions);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to read archive snapshot.", detail = ex.Message });
        }

        if (snapshot?.Organization == null)
            return BadRequest(new { error = "Archive snapshot is invalid." });

        if (await _db.Organizations.AnyAsync(o => o.OrganizationGuid == archive.OriginalOrganizationGuid))
            return BadRequest(new { error = "An organization with this GUID already exists. Restore aborted." });

        var now = DateTime.UtcNow;
        var callerId = GetCurrentUserId() ?? "";
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var oldOrgId = snapshot.Organization.Id;

            var restoredOrg = new Organization
            {
                OrganizationGuid = archive.OriginalOrganizationGuid,
                Name = snapshot.Organization.Name,
                LogoUrl = snapshot.Organization.LogoUrl,
                CreatedAt = snapshot.Organization.CreatedAt,
                Plan = snapshot.Organization.Plan,
                EnterpriseExtraTokenPacks = snapshot.Organization.EnterpriseExtraTokenPacks,
                PurchasedLicenses = snapshot.Organization.PurchasedLicenses,
                PurchasedProfessionalLicenses = snapshot.Organization.PurchasedProfessionalLicenses,
                PurchasedEnterpriseLicenses = snapshot.Organization.PurchasedEnterpriseLicenses,
                PayPalSubscriptionId = snapshot.Organization.PayPalSubscriptionId,
                PayPalPlanId = snapshot.Organization.PayPalPlanId,
                SubscriptionStatus = snapshot.Organization.SubscriptionStatus,
                SubscriptionStartDate = snapshot.Organization.SubscriptionStartDate,
                SubscriptionNextBillingDate = snapshot.Organization.SubscriptionNextBillingDate,
                PayPalProSubscriptionId = snapshot.Organization.PayPalProSubscriptionId,
                PayPalEntSubscriptionId = snapshot.Organization.PayPalEntSubscriptionId,
                FailedPaymentCount = snapshot.Organization.FailedPaymentCount,
                GraceUntil = snapshot.Organization.GraceUntil,
                IsEmailVerified = snapshot.Organization.IsEmailVerified,
                EmailVerificationToken = snapshot.Organization.EmailVerificationToken,
                EmailVerificationTokenExpiry = snapshot.Organization.EmailVerificationTokenExpiry,
                LicenseStartsAt = snapshot.Organization.LicenseStartsAt,
                LicenseEndsAt = snapshot.Organization.LicenseEndsAt,
                AutoRenew = snapshot.Organization.AutoRenew,
                LicenseNotes = snapshot.Organization.LicenseNotes,
                IsBlocked = snapshot.Organization.IsBlocked,
                BlockedReason = snapshot.Organization.BlockedReason,
                BlockedAt = snapshot.Organization.BlockedAt
            };
            _db.Organizations.Add(restoredOrg);
            await _db.SaveChangesAsync();
            var newOrgId = restoredOrg.Id;

            var oldToNewUserIds = new Dictionary<string, string>(StringComparer.Ordinal);
            if (snapshot.Users.Count > 0)
            {
                var oldIds = snapshot.Users.Select(u => u.Id).ToList();
                var existingIds = await _db.Users
                    .Where(u => oldIds.Contains(u.Id))
                    .Select(u => u.Id)
                    .ToListAsync();
                var existingSet = existingIds.ToHashSet(StringComparer.Ordinal);

                var usersToInsert = new List<ApplicationUser>();
                foreach (var old in snapshot.Users)
                {
                    var userIdCollision = existingSet.Contains(old.Id);
                    var newUserId = userIdCollision ? Guid.NewGuid().ToString() : old.Id;
                    oldToNewUserIds[old.Id] = newUserId;

                    var restoredUserName = old.UserName;
                    var restoredNormalizedUserName = old.NormalizedUserName;
                    if (userIdCollision)
                    {
                        var suffix = newUserId[..8];
                        restoredUserName = string.IsNullOrWhiteSpace(old.UserName)
                            ? $"restored-{suffix}"
                            : $"{old.UserName}.restored.{suffix}";
                        restoredNormalizedUserName = restoredUserName.ToUpperInvariant();
                    }

                    usersToInsert.Add(new ApplicationUser
                    {
                        Id = newUserId,
                        UserName = restoredUserName,
                        NormalizedUserName = restoredNormalizedUserName,
                        Email = old.Email,
                        NormalizedEmail = old.NormalizedEmail,
                        EmailConfirmed = old.EmailConfirmed,
                        PasswordHash = old.PasswordHash,
                        SecurityStamp = old.SecurityStamp,
                        ConcurrencyStamp = old.ConcurrencyStamp,
                        PhoneNumber = old.PhoneNumber,
                        PhoneNumberConfirmed = old.PhoneNumberConfirmed,
                        TwoFactorEnabled = old.TwoFactorEnabled,
                        LockoutEnd = old.LockoutEnd,
                        LockoutEnabled = old.LockoutEnabled,
                        AccessFailedCount = old.AccessFailedCount,
                        FullName = old.FullName,
                        Role = old.Role,
                        OrganizationId = newOrgId,
                        Status = old.Status,
                        CreatedAt = old.CreatedAt,
                        LastSeenAt = old.LastSeenAt,
                        StripeCustomerId = old.StripeCustomerId,
                        CardBrand = old.CardBrand,
                        CardLast4 = old.CardLast4,
                        LastLoginIp = old.LastLoginIp,
                        LastLoginCountry = old.LastLoginCountry,
                        LastLoginCity = old.LastLoginCity,
                        LastLoginAt = old.LastLoginAt,
                        MustChangePassword = old.MustChangePassword
                    });
                }

                _db.Users.AddRange(usersToInsert);
                await _db.SaveChangesAsync();
            }

            if (snapshot.SubscriptionPlans.Count > 0)
            {
                var plans = snapshot.SubscriptionPlans
                    .Where(s => oldToNewUserIds.ContainsKey(s.UserId))
                    .Select(s => new SubscriptionPlan
                    {
                        UserId = oldToNewUserIds[s.UserId],
                        Plan = s.Plan,
                        TrialStartDate = s.TrialStartDate,
                        TrialEndDate = s.TrialEndDate,
                        HasUsedTrial = s.HasUsedTrial,
                        CreatedAt = s.CreatedAt
                    })
                    .ToList();
                _db.SubscriptionPlans.AddRange(plans);
                await _db.SaveChangesAsync();
            }

            var workspaceMap = new Dictionary<int, int>();
            if (snapshot.Workspaces.Count > 0)
            {
                var workspaces = snapshot.Workspaces.Select(w => new Workspace
                {
                    Guid = w.Guid,
                    Name = w.Name,
                    Description = w.Description,
                    LogoUrl = w.LogoUrl,
                    OwnerId = w.OwnerId != null && oldToNewUserIds.TryGetValue(w.OwnerId, out var ownerId) ? ownerId : w.OwnerId,
                    OrganizationId = newOrgId,
                    CreatedAt = w.CreatedAt
                }).ToList();

                _db.Workspaces.AddRange(workspaces);
                await _db.SaveChangesAsync();
                for (var i = 0; i < workspaces.Count; i++) workspaceMap[snapshot.Workspaces[i].Id] = workspaces[i].Id;
            }

            var datasourceMap = new Dictionary<int, int>();
            if (snapshot.WorkspaceMemories.Count > 0)
            {
                var memories = snapshot.WorkspaceMemories
                    .Where(m => workspaceMap.ContainsKey(m.WorkspaceId))
                    .Select(m => new WorkspaceMemory
                    {
                        WorkspaceId = workspaceMap[m.WorkspaceId],
                        Content = m.Content,
                        Source = m.Source,
                        Category = m.Category,
                        CreatedAt = m.CreatedAt
                    })
                    .ToList();
                _db.WorkspaceMemories.AddRange(memories);
                await _db.SaveChangesAsync();
            }

            if (snapshot.Datasources.Count > 0)
            {
                var datasources = snapshot.Datasources.Select(d => new Datasource
                {
                    Guid = d.Guid,
                    Name = d.Name,
                    Type = d.Type,
                    ConnectionString = d.ConnectionString,
                    DbUser = d.DbUser,
                    DbPassword = d.DbPassword,
                    SelectedTables = d.SelectedTables,
                    XmlaEndpoint = d.XmlaEndpoint,
                    MicrosoftAccountTenantId = d.MicrosoftAccountTenantId,
                    ApiUrl = d.ApiUrl,
                    ApiKey = d.ApiKey,
                    ApiMethod = d.ApiMethod,
                    TransformEnabled = d.TransformEnabled,
                    TransformToml = d.TransformToml,
                    OrganizationId = newOrgId,
                    WorkspaceId = d.WorkspaceId.HasValue && workspaceMap.TryGetValue(d.WorkspaceId.Value, out var wsId) ? wsId : null,
                    CreatedAt = d.CreatedAt
                }).ToList();

                _db.Datasources.AddRange(datasources);
                await _db.SaveChangesAsync();
                for (var i = 0; i < datasources.Count; i++) datasourceMap[snapshot.Datasources[i].Id] = datasources[i].Id;
            }

            var agentMap = new Dictionary<int, int>();
            if (snapshot.Agents.Count > 0)
            {
                var agents = snapshot.Agents.Select(a => new Agent
                {
                    Guid = a.Guid,
                    Name = a.Name,
                    SystemPrompt = a.SystemPrompt,
                    DatasourceId = a.DatasourceId.HasValue && datasourceMap.TryGetValue(a.DatasourceId.Value, out var dsId) ? dsId : null,
                    WorkspaceId = a.WorkspaceId.HasValue && workspaceMap.TryGetValue(a.WorkspaceId.Value, out var wsId) ? wsId : null,
                    OrganizationId = newOrgId,
                    CreatedAt = a.CreatedAt
                }).ToList();

                _db.Agents.AddRange(agents);
                await _db.SaveChangesAsync();
                for (var i = 0; i < agents.Count; i++) agentMap[snapshot.Agents[i].Id] = agents[i].Id;
            }

            var dashboardMap = new Dictionary<int, int>();
            if (snapshot.Dashboards.Count > 0)
            {
                var dashboards = snapshot.Dashboards
                    .Where(d => workspaceMap.ContainsKey(d.WorkspaceId))
                    .Select(d => new Dashboard
                    {
                        Guid = d.Guid,
                        Name = d.Name,
                        WorkspaceId = workspaceMap[d.WorkspaceId],
                        AgentId = d.AgentId.HasValue && agentMap.TryGetValue(d.AgentId.Value, out var aId) ? aId : null,
                        DatasourceId = d.DatasourceId.HasValue && datasourceMap.TryGetValue(d.DatasourceId.Value, out var dsId) ? dsId : null,
                        CreatedAt = d.CreatedAt
                    }).ToList();

                _db.Dashboards.AddRange(dashboards);
                await _db.SaveChangesAsync();
                var source = snapshot.Dashboards.Where(d => workspaceMap.ContainsKey(d.WorkspaceId)).ToList();
                for (var i = 0; i < dashboards.Count; i++) dashboardMap[source[i].Id] = dashboards[i].Id;
            }

            var reportMap = new Dictionary<int, int>();
            if (snapshot.Reports.Count > 0)
            {
                var reports = snapshot.Reports
                    .Where(r => workspaceMap.ContainsKey(r.WorkspaceId))
                    .Select(r => new Report
                    {
                        Guid = r.Guid,
                        Name = r.Name,
                        WorkspaceId = workspaceMap[r.WorkspaceId],
                        DashboardId = r.DashboardId.HasValue && dashboardMap.TryGetValue(r.DashboardId.Value, out var dId) ? dId : null,
                        DatasourceId = r.DatasourceId.HasValue && datasourceMap.TryGetValue(r.DatasourceId.Value, out var dsId) ? dsId : null,
                        AgentId = r.AgentId.HasValue && agentMap.TryGetValue(r.AgentId.Value, out var aId) ? aId : null,
                        ChartIds = r.ChartIds,
                        CanvasJson = r.CanvasJson,
                        Status = r.Status,
                        ShareToken = r.ShareToken,
                        EmbedTokenVersion = r.EmbedTokenVersion,
                        CreatedBy = r.CreatedBy != null && oldToNewUserIds.TryGetValue(r.CreatedBy, out var createdBy) ? createdBy : r.CreatedBy,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt
                    }).ToList();

                _db.Reports.AddRange(reports);
                await _db.SaveChangesAsync();
                var source = snapshot.Reports.Where(r => workspaceMap.ContainsKey(r.WorkspaceId)).ToList();
                for (var i = 0; i < reports.Count; i++) reportMap[source[i].Id] = reports[i].Id;
            }

            if (snapshot.ReportRevisions.Count > 0)
            {
                var revisions = snapshot.ReportRevisions
                    .Where(rr => reportMap.ContainsKey(rr.ReportId))
                    .Select(rr => new ReportRevision
                    {
                        ReportId = reportMap[rr.ReportId],
                        Kind = rr.Kind,
                        Name = rr.Name,
                        CanvasJson = rr.CanvasJson,
                        ReportName = rr.ReportName,
                        CreatedBy = rr.CreatedBy != null && oldToNewUserIds.TryGetValue(rr.CreatedBy, out var createdBy) ? createdBy : rr.CreatedBy,
                        CreatedAt = rr.CreatedAt
                    })
                    .ToList();
                _db.ReportRevisions.AddRange(revisions);
                await _db.SaveChangesAsync();
            }

            if (snapshot.SharedReports.Count > 0)
            {
                var sharedReports = snapshot.SharedReports
                    .Where(sr => reportMap.ContainsKey(sr.ReportId) && oldToNewUserIds.ContainsKey(sr.UserId))
                    .Select(sr => new SharedReport
                    {
                        ReportId = reportMap[sr.ReportId],
                        UserId = oldToNewUserIds[sr.UserId],
                        SharedAt = sr.SharedAt
                    })
                    .ToList();
                _db.SharedReports.AddRange(sharedReports);
                await _db.SaveChangesAsync();
            }

            var chatMessageMap = new Dictionary<int, int>();
            if (snapshot.ChatMessages.Count > 0)
            {
                var chatMessages = snapshot.ChatMessages
                    .Where(m => workspaceMap.ContainsKey(m.WorkspaceId) && oldToNewUserIds.ContainsKey(m.UserId))
                    .Select(m => new ChatMessage
                    {
                        Role = m.Role,
                        Content = m.Content,
                        GeneratedQuery = m.GeneratedQuery,
                        QueryDescription = m.QueryDescription,
                        ResultJson = m.ResultJson,
                        IsPinned = m.IsPinned,
                        WorkspaceId = workspaceMap[m.WorkspaceId],
                        AgentId = m.AgentId,
                        UserId = oldToNewUserIds[m.UserId],
                        CreatedAt = m.CreatedAt
                    })
                    .ToList();
                _db.ChatMessages.AddRange(chatMessages);
                await _db.SaveChangesAsync();
                var source = snapshot.ChatMessages
                    .Where(m => workspaceMap.ContainsKey(m.WorkspaceId) && oldToNewUserIds.ContainsKey(m.UserId))
                    .ToList();
                for (var i = 0; i < chatMessages.Count; i++) chatMessageMap[source[i].Id] = chatMessages[i].Id;
            }

            if (snapshot.PinnedResults.Count > 0)
            {
                var pinnedResults = snapshot.PinnedResults
                    .Where(p => workspaceMap.ContainsKey(p.WorkspaceId)
                                && oldToNewUserIds.ContainsKey(p.UserId)
                                && chatMessageMap.ContainsKey(p.ChatMessageId))
                    .Select(p => new PinnedResult
                    {
                        DatasetName = p.DatasetName,
                        JsonData = p.JsonData,
                        ChatMessageId = chatMessageMap[p.ChatMessageId],
                        WorkspaceId = workspaceMap[p.WorkspaceId],
                        UserId = oldToNewUserIds[p.UserId],
                        CreatedAt = p.CreatedAt
                    })
                    .ToList();
                _db.PinnedResults.AddRange(pinnedResults);
                await _db.SaveChangesAsync();
            }

            if (snapshot.WorkspaceUsers.Count > 0)
            {
                var workspaceUsers = snapshot.WorkspaceUsers
                    .Where(wu => workspaceMap.ContainsKey(wu.WorkspaceId) && oldToNewUserIds.ContainsKey(wu.UserId))
                    .Select(wu => new WorkspaceUser
                    {
                        WorkspaceId = workspaceMap[wu.WorkspaceId],
                        UserId = oldToNewUserIds[wu.UserId],
                        Role = wu.Role,
                        CreatedAt = wu.CreatedAt
                    })
                    .ToList();
                _db.WorkspaceUsers.AddRange(workspaceUsers);
                await _db.SaveChangesAsync();
            }

            var notificationMap = new Dictionary<int, int>();
            if (snapshot.Notifications.Count > 0)
            {
                var notifications = snapshot.Notifications.Select(n => new Notification
                {
                    Scope = n.Scope,
                    OrganizationId = n.OrganizationId == oldOrgId ? newOrgId : n.OrganizationId,
                    TargetUserId = n.TargetUserId != null && oldToNewUserIds.TryGetValue(n.TargetUserId, out var targetUserId) ? targetUserId : n.TargetUserId,
                    TargetUserIdsCsv = RemapUserIdsCsv(n.TargetUserIdsCsv, oldToNewUserIds),
                    TargetRolesCsv = n.TargetRolesCsv,
                    Title = n.Title,
                    Body = n.Body,
                    Type = n.Type,
                    Severity = n.Severity,
                    Link = n.Link,
                    CreatedAt = n.CreatedAt,
                    ExpiresAt = n.ExpiresAt,
                    CreatedByUserId = n.CreatedByUserId != null && oldToNewUserIds.TryGetValue(n.CreatedByUserId, out var createdById) ? createdById : n.CreatedByUserId,
                    CreatedByRole = n.CreatedByRole,
                    SystemKey = n.SystemKey,
                    ScheduleAt = n.ScheduleAt,
                    DeliveredAt = n.DeliveredAt,
                    DeliveryStatus = n.DeliveryStatus,
                    IsRecalled = n.IsRecalled,
                    RecalledAt = n.RecalledAt,
                    RecalledByUserId = n.RecalledByUserId != null && oldToNewUserIds.TryGetValue(n.RecalledByUserId, out var recalledById) ? recalledById : n.RecalledByUserId
                }).ToList();

                _db.Notifications.AddRange(notifications);
                await _db.SaveChangesAsync();
                for (var i = 0; i < notifications.Count; i++) notificationMap[snapshot.Notifications[i].Id] = notifications[i].Id;
            }

            if (snapshot.UserNotifications.Count > 0)
            {
                var userNotifications = snapshot.UserNotifications
                    .Where(un => oldToNewUserIds.ContainsKey(un.UserId) && notificationMap.ContainsKey(un.NotificationId))
                    .Select(un => new UserNotification
                    {
                        UserId = oldToNewUserIds[un.UserId],
                        NotificationId = notificationMap[un.NotificationId],
                        ReadAt = un.ReadAt,
                        DismissedAt = un.DismissedAt,
                        IsClicked = un.IsClicked,
                        ClickedAt = un.ClickedAt,
                        EmailSent = un.EmailSent
                    })
                    .ToList();
                _db.UserNotifications.AddRange(userNotifications);
                await _db.SaveChangesAsync();
            }

            if (snapshot.SupportTickets.Count > 0)
            {
                var tickets = snapshot.SupportTickets.Select(t => new SupportTicket
                {
                    TicketNumber = t.TicketNumber,
                    OrganizationId = t.OrganizationId == oldOrgId ? newOrgId : t.OrganizationId,
                    UserId = t.UserId != null && oldToNewUserIds.TryGetValue(t.UserId, out var ticketUserId) ? ticketUserId : t.UserId,
                    RequesterName = t.RequesterName,
                    RequesterEmail = t.RequesterEmail,
                    Category = t.Category,
                    Priority = t.Priority,
                    Subject = t.Subject,
                    Message = t.Message,
                    Status = t.Status,
                    CreatedAt = t.CreatedAt,
                    ResolvedAt = t.ResolvedAt,
                    AssignedToUserId = t.AssignedToUserId != null && oldToNewUserIds.TryGetValue(t.AssignedToUserId, out var assignedToId) ? assignedToId : t.AssignedToUserId,
                    Response = t.Response
                }).ToList();
                _db.SupportTickets.AddRange(tickets);
                await _db.SaveChangesAsync();
            }

            if (snapshot.TokenUsages.Count > 0)
            {
                var tokenUsages = snapshot.TokenUsages.Select(t => new TokenUsage
                {
                    OrganizationId = newOrgId,
                    UserId = oldToNewUserIds.TryGetValue(t.UserId, out var tokenUserId) ? tokenUserId : t.UserId,
                    TokensUsed = t.TokensUsed,
                    Year = t.Year,
                    Month = t.Month,
                    CreatedAt = t.CreatedAt
                }).ToList();
                _db.TokenUsages.AddRange(tokenUsages);
                await _db.SaveChangesAsync();
            }

            if (snapshot.PaymentRecords.Count > 0)
            {
                var payments = snapshot.PaymentRecords.Select(p => new PaymentRecord
                {
                    OrganizationId = newOrgId,
                    UserId = p.UserId != null && oldToNewUserIds.TryGetValue(p.UserId, out var payUserId) ? payUserId : p.UserId,
                    PaymentType = p.PaymentType,
                    PaymentMethod = p.PaymentMethod,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    Status = p.Status,
                    PayPalOrderId = p.PayPalOrderId,
                    PayPalSubscriptionId = p.PayPalSubscriptionId,
                    PayPalEventId = p.PayPalEventId,
                    Description = p.Description,
                    ErrorMessage = p.ErrorMessage,
                    PlanKey = p.PlanKey,
                    CreatedAt = p.CreatedAt,
                    InvoiceNumber = p.InvoiceNumber,
                    Quantity = p.Quantity,
                    UnitPrice = p.UnitPrice,
                    Subtotal = p.Subtotal,
                    TaxAmount = p.TaxAmount,
                    TaxRegion = p.TaxRegion,
                    TaxRatePercent = p.TaxRatePercent,
                    TokensAdded = p.TokensAdded,
                    BillingName = p.BillingName,
                    BillingEmail = p.BillingEmail,
                    BillingCompany = p.BillingCompany,
                    BillingAddressLine1 = p.BillingAddressLine1,
                    BillingAddressLine2 = p.BillingAddressLine2,
                    BillingCity = p.BillingCity,
                    BillingState = p.BillingState,
                    BillingPostalCode = p.BillingPostalCode,
                    BillingCountry = p.BillingCountry,
                    LineItemsJson = p.LineItemsJson,
                    PayerEmail = p.PayerEmail,
                    PayerName = p.PayerName,
                    CaptureId = p.CaptureId,
                    PdfPath = p.PdfPath,
                    PaidAt = p.PaidAt,
                    DueDate = p.DueDate
                }).ToList();
                _db.PaymentRecords.AddRange(payments);
                await _db.SaveChangesAsync();
            }

            if (snapshot.PlanChangeLogs.Count > 0)
            {
                var planLogs = snapshot.PlanChangeLogs.Select(p => new PlanChangeLog
                {
                    OrganizationId = newOrgId,
                    FromPlan = p.FromPlan,
                    ToPlan = p.ToPlan,
                    FromPurchasedLicenses = p.FromPurchasedLicenses,
                    ToPurchasedLicenses = p.ToPurchasedLicenses,
                    FromLicenseEndsAt = p.FromLicenseEndsAt,
                    ToLicenseEndsAt = p.ToLicenseEndsAt,
                    ChangeType = p.ChangeType,
                    Reason = p.Reason,
                    ChangedByUserId = p.ChangedByUserId != null && oldToNewUserIds.TryGetValue(p.ChangedByUserId, out var changedById) ? changedById : p.ChangedByUserId,
                    ChangedByEmail = p.ChangedByEmail,
                    CreatedAt = p.CreatedAt
                }).ToList();
                _db.PlanChangeLogs.AddRange(planLogs);
                await _db.SaveChangesAsync();
            }

            if (snapshot.ActivityLogs.Count > 0)
            {
                var logs = snapshot.ActivityLogs.Select(l => new ActivityLog
                {
                    Action = l.Action,
                    Description = l.Description,
                    UserId = oldToNewUserIds.TryGetValue(l.UserId, out var logUserId) ? logUserId : l.UserId,
                    OrganizationId = l.OrganizationId == oldOrgId ? newOrgId : l.OrganizationId,
                    CreatedAt = l.CreatedAt
                }).ToList();
                _db.ActivityLogs.AddRange(logs);
                await _db.SaveChangesAsync();
            }

            archive.RestoredAt = now;
            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "OrganizationRestored",
                Description = $"SuperAdmin restored organization '{archive.Name}' from archive #{archive.Id}.",
                UserId = callerId,
                OrganizationId = newOrgId,
                CreatedAt = now
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new { success = true, archiveId, restoredOrganizationId = newOrgId, organizationGuid = archive.OriginalOrganizationGuid });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { error = "Failed to restore organization archive.", detail = ex.Message });
        }
    }

    [HttpPost("/api/admin/super/orgs/trash/{archiveId}/purge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PurgeOrganizationArchive(int archiveId)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var archive = await _db.OrganizationDeletionArchives.FirstOrDefaultAsync(a => a.Id == archiveId);
        if (archive == null) return NotFound();

        var callerId = GetCurrentUserId() ?? "";
        _db.OrganizationDeletionArchives.Remove(archive);
        _db.ActivityLogs.Add(new ActivityLog
        {
            Action = "OrganizationArchivePurged",
            Description = $"SuperAdmin purged organization archive #{archive.Id} for '{archive.Name}' (GUID={archive.OriginalOrganizationGuid}).",
            UserId = callerId,
            OrganizationId = null,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true, archiveId });
    }

    private static string BuildDisambiguationToken(string name, Guid organizationGuid)
    {
        var compactGuid = organizationGuid.ToString("N");
        return $"{name}#{compactGuid[^4..]}";
    }

    private static string? RemapUserIdsCsv(string? csv, IReadOnlyDictionary<string, string> userIdMap)
    {
        if (string.IsNullOrWhiteSpace(csv)) return csv;
        var remapped = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => userIdMap.TryGetValue(id, out var mapped) ? mapped : id)
            .ToArray();
        return string.Join(",", remapped);
    }

    private async Task<OrganizationDeletionSnapshot> BuildDeletionSnapshotAsync(
        int organizationId,
        IReadOnlyList<string> userIds,
        IReadOnlyList<int> workspaceIds,
        IReadOnlyList<int> reportIds)
    {
        var snapshot = new OrganizationDeletionSnapshot
        {
            Organization = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == organizationId),
            Users = userIds.Count > 0
                ? await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToListAsync()
                : new List<ApplicationUser>(),
            SubscriptionPlans = userIds.Count > 0
                ? await _db.SubscriptionPlans.AsNoTracking().Where(s => userIds.Contains(s.UserId)).ToListAsync()
                : new List<SubscriptionPlan>(),
            Workspaces = await _db.Workspaces.AsNoTracking().Where(w => w.OrganizationId == organizationId).ToListAsync(),
            WorkspaceMemories = workspaceIds.Count > 0
                ? await _db.WorkspaceMemories.AsNoTracking().Where(m => workspaceIds.Contains(m.WorkspaceId)).ToListAsync()
                : new List<WorkspaceMemory>(),
            Agents = await _db.Agents.AsNoTracking().Where(a => a.OrganizationId == organizationId).ToListAsync(),
            Datasources = await _db.Datasources.AsNoTracking().Where(d => d.OrganizationId == organizationId).ToListAsync(),
            Reports = workspaceIds.Count > 0
                ? await _db.Reports.AsNoTracking().Where(r => workspaceIds.Contains(r.WorkspaceId)).ToListAsync()
                : new List<Report>(),
            Dashboards = workspaceIds.Count > 0
                ? await _db.Dashboards.AsNoTracking().Where(d => workspaceIds.Contains(d.WorkspaceId)).ToListAsync()
                : new List<Dashboard>(),
            ChatMessages = (workspaceIds.Count > 0 || userIds.Count > 0)
                ? await _db.ChatMessages.AsNoTracking()
                    .Where(m => (workspaceIds.Count > 0 && workspaceIds.Contains(m.WorkspaceId))
                                || (userIds.Count > 0 && userIds.Contains(m.UserId)))
                    .ToListAsync()
                : new List<ChatMessage>(),
            PinnedResults = (workspaceIds.Count > 0 || userIds.Count > 0)
                ? await _db.PinnedResults.AsNoTracking()
                    .Where(p => (workspaceIds.Count > 0 && workspaceIds.Contains(p.WorkspaceId))
                                || (userIds.Count > 0 && userIds.Contains(p.UserId)))
                    .ToListAsync()
                : new List<PinnedResult>(),
            TokenUsages = await _db.TokenUsages.AsNoTracking().Where(t => t.OrganizationId == organizationId).ToListAsync(),
            PaymentRecords = await _db.PaymentRecords.AsNoTracking().Where(p => p.OrganizationId == organizationId).ToListAsync(),
            PlanChangeLogs = await _db.PlanChangeLogs.AsNoTracking().Where(p => p.OrganizationId == organizationId).ToListAsync(),
            Notifications = userIds.Count > 0
                ? await _db.Notifications.AsNoTracking()
                    .Where(n => n.OrganizationId == organizationId
                                || (n.TargetUserId != null && userIds.Contains(n.TargetUserId)))
                    .ToListAsync()
                : await _db.Notifications.AsNoTracking().Where(n => n.OrganizationId == organizationId).ToListAsync(),
            UserNotifications = userIds.Count > 0
                ? await _db.UserNotifications.AsNoTracking().Where(n => userIds.Contains(n.UserId)).ToListAsync()
                : new List<UserNotification>(),
            SupportTickets = userIds.Count > 0
                ? await _db.SupportTickets.AsNoTracking()
                    .Where(t => t.OrganizationId == organizationId
                                || (t.UserId != null && userIds.Contains(t.UserId))
                                || (t.AssignedToUserId != null && userIds.Contains(t.AssignedToUserId)))
                    .ToListAsync()
                : await _db.SupportTickets.AsNoTracking().Where(t => t.OrganizationId == organizationId).ToListAsync(),
            ActivityLogs = userIds.Count > 0
                ? await _db.ActivityLogs.AsNoTracking()
                    .Where(l => l.OrganizationId == organizationId || (l.UserId != null && userIds.Contains(l.UserId)))
                    .ToListAsync()
                : await _db.ActivityLogs.AsNoTracking().Where(l => l.OrganizationId == organizationId).ToListAsync(),
            WorkspaceUsers = userIds.Count > 0
                ? await _db.WorkspaceUsers.AsNoTracking().Where(wu => userIds.Contains(wu.UserId)).ToListAsync()
                : new List<WorkspaceUser>(),
            SharedReports = reportIds.Count > 0
                ? await _db.SharedReports.AsNoTracking().Where(sr => reportIds.Contains(sr.ReportId)).ToListAsync()
                : new List<SharedReport>(),
            ReportRevisions = reportIds.Count > 0
                ? await _db.ReportRevisions.AsNoTracking().Where(rr => reportIds.Contains(rr.ReportId)).ToListAsync()
                : new List<ReportRevision>()
        };

        return snapshot;
    }

    private static readonly JsonSerializerOptions OrganizationArchiveJsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private sealed class OrganizationDeletionSnapshot
    {
        public Organization Organization { get; set; } = new();
        public List<ApplicationUser> Users { get; set; } = new();
        public List<SubscriptionPlan> SubscriptionPlans { get; set; } = new();
        public List<Workspace> Workspaces { get; set; } = new();
        public List<WorkspaceMemory> WorkspaceMemories { get; set; } = new();
        public List<Agent> Agents { get; set; } = new();
        public List<Datasource> Datasources { get; set; } = new();
        public List<Report> Reports { get; set; } = new();
        public List<Dashboard> Dashboards { get; set; } = new();
        public List<ChatMessage> ChatMessages { get; set; } = new();
        public List<PinnedResult> PinnedResults { get; set; } = new();
        public List<TokenUsage> TokenUsages { get; set; } = new();
        public List<PaymentRecord> PaymentRecords { get; set; } = new();
        public List<PlanChangeLog> PlanChangeLogs { get; set; } = new();
        public List<Notification> Notifications { get; set; } = new();
        public List<UserNotification> UserNotifications { get; set; } = new();
        public List<SupportTicket> SupportTickets { get; set; } = new();
        public List<ActivityLog> ActivityLogs { get; set; } = new();
        public List<WorkspaceUser> WorkspaceUsers { get; set; } = new();
        public List<SharedReport> SharedReports { get; set; } = new();
        public List<ReportRevision> ReportRevisions { get; set; } = new();
    }

    [HttpGet("/superadmin/activity")]
    public async Task<IActionResult> ActivityLogs([FromQuery] int page = 1, [FromQuery] string? search = null)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        const int pageSize = 50;

        var query = from log in _db.ActivityLogs
                    join user in _db.Users on log.UserId equals user.Id into uj
                    from user in uj.DefaultIfEmpty()
                    join org in _db.Organizations on log.OrganizationId equals org.Id into oj
                    from org in oj.DefaultIfEmpty()
                    select new ActivityLogViewModel
                    {
                        Id = log.Id,
                        Action = log.Action,
                        Description = log.Description,
                        CreatedAt = log.CreatedAt,
                        UserId = log.UserId,
                        UserName = user != null ? user.FullName : null,
                        UserEmail = user != null ? user.Email : null,
                        OrganizationId = log.OrganizationId,
                        OrganizationName = org != null ? org.Name : null
                    };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(l =>
                (l.UserName != null && l.UserName.ToLower().Contains(term)) ||
                (l.UserEmail != null && l.UserEmail.ToLower().Contains(term)) ||
                (l.OrganizationName != null && l.OrganizationName.ToLower().Contains(term)) ||
                l.Action.ToLower().Contains(term) ||
                l.Description.ToLower().Contains(term));
        }

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Page = page;
        ViewBag.Search = search;
        return View("~/Views/Admin/ActivityLogs.cshtml", logs);
    }

    public class ActivityLogViewModel
    {
        public int Id { get; set; }
        public string Action { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string UserId { get; set; } = "";
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
        public int? OrganizationId { get; set; }
        public string? OrganizationName { get; set; }
    }

    public class UpdateOrgPlanRequest
    {
        public string Plan { get; set; } = "";
        public int? PurchasedLicenses { get; set; }
    }

    [HttpGet("/superadmin/aiconfig")]
    public async Task<IActionResult> AiConfig()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        return View("~/Views/Admin/AiConfig.cshtml");
    }

    [HttpGet("/superadmin/revenue")]
    public async Task<IActionResult> Payments()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var plans = await _db.SubscriptionPlans
            .Include(p => p.User)
            .ToListAsync();

        var proCount = plans.Count(p => p.Plan == PlanType.Professional);
        var enterpriseCount = plans.Count(p => p.Plan == PlanType.Enterprise);
        ViewBag.ProCount = proCount;
        ViewBag.EnterpriseCount = enterpriseCount;
        ViewBag.ProRevenue = proCount * PlanPricing.ProPricePerUser;
        ViewBag.EnterpriseRevenue = enterpriseCount * PlanPricing.EnterprisePricePerUser;
        ViewBag.TotalIncome = proCount * PlanPricing.ProPricePerUser + enterpriseCount * PlanPricing.EnterprisePricePerUser;
        ViewBag.ActiveTrials = plans.Count(p => p.IsTrialActive);
        ViewBag.ExpiredTrials = plans.Count(p => p.IsTrialExpired);

        var paidUsers = await _db.Users
            .Where(u => u.CardLast4 != null)
            .Select(u => new { u.Id, u.FullName, u.Email, u.CardBrand, u.CardLast4 })
            .ToListAsync();
        ViewBag.PaidUsers = paidUsers;

        // Actual collected revenue from PaymentRecords (lifetime + this month) – not just current MRR.
        var nowUtc = DateTime.UtcNow;
        var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var succeeded = _db.PaymentRecords.Where(p => p.Status == "succeeded");
        ViewBag.PaymentsCollectedAll = await succeeded.SumAsync(p => (decimal?)p.Amount) ?? 0m;
        ViewBag.PaymentsCollectedThisMonth = await succeeded
            .Where(p => p.CreatedAt >= monthStart)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        ViewBag.PaymentsCount = await succeeded.CountAsync();

        ViewBag.RecentPayments = await _db.PaymentRecords
            .OrderByDescending(p => p.CreatedAt)
            .Take(25)
            .Select(p => new
            {
                p.Id,
                p.InvoiceNumber,
                p.CreatedAt,
                p.PaymentType,
                p.PaymentMethod,
                p.Amount,
                p.Currency,
                p.Status,
                p.PayerEmail,
                p.Description
            })
            .ToListAsync();

        return View("~/Views/Admin/Revenue.cshtml");
    }

    [HttpGet("/superadmin/seo")]
    public async Task<IActionResult> Seo()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var entries = await _db.SeoEntries.OrderBy(s => s.PageUrl).ToListAsync();
        return View("~/Views/Admin/Seo.cshtml", entries);
    }

    [HttpPost("/superadmin/seo/save")]
    public async Task<IActionResult> SaveSeo([FromBody] SeoEntry entry)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        if (entry.Id > 0)
        {
            var existing = await _db.SeoEntries.FindAsync(entry.Id);
            if (existing == null) return NotFound();
            existing.Title = entry.Title;
            existing.MetaDescription = entry.MetaDescription;
            existing.MetaKeywords = entry.MetaKeywords;
            existing.OgTitle = entry.OgTitle;
            existing.OgDescription = entry.OgDescription;
            existing.RobotsDirective = entry.RobotsDirective;
            existing.LastModified = DateTime.UtcNow;
        }
        else
        {
            entry.LastModified = DateTime.UtcNow;
            entry.CreatedAt = DateTime.UtcNow;
            _db.SeoEntries.Add(entry);
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/superadmin/seo/autofill")]
    public async Task<IActionResult> AutoFillSeo()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var existing = await _db.SeoEntries.Select(s => s.PageUrl).ToListAsync();

        var defaults = new List<SeoEntry>
        {
            new() { PageUrl = "/", Title = "ChatPortal – AI-Powered Team Collaboration", MetaDescription = "Real-time AI chat, dashboards, and analytics for modern teams.", MetaKeywords = "chat, AI, collaboration, team, analytics", OgTitle = "ChatPortal – AI-Powered Team Collaboration", OgDescription = "Real-time AI chat, dashboards, and analytics for modern teams.", RobotsDirective = "index, follow" },
            new() { PageUrl = "/about", Title = "About – ChatPortal", MetaDescription = "Learn about ChatPortal's mission to empower teams with AI-driven collaboration.", MetaKeywords = "about, chatportal, team, mission", OgTitle = "About ChatPortal", OgDescription = "Our mission to empower teams with AI-driven collaboration.", RobotsDirective = "index, follow" },
            new() { PageUrl = "/pricing", Title = "Pricing – ChatPortal", MetaDescription = "Flexible pricing plans for teams of all sizes. Professional and Enterprise tiers available.", MetaKeywords = "pricing, plans, professional, enterprise", OgTitle = "ChatPortal Pricing", OgDescription = "Flexible pricing plans for teams of all sizes.", RobotsDirective = "index, follow" },
            new() { PageUrl = "/docs", Title = "Documentation – ChatPortal", MetaDescription = "Guides, API references, and tutorials to get started with ChatPortal.", MetaKeywords = "docs, documentation, API, guides, tutorials", OgTitle = "ChatPortal Documentation", OgDescription = "Guides, API references, and tutorials.", RobotsDirective = "index, follow" },
            new() { PageUrl = "/blog", Title = "Blog – ChatPortal", MetaDescription = "Latest news, tips, and product updates from the ChatPortal team.", MetaKeywords = "blog, news, updates, tips", OgTitle = "ChatPortal Blog", OgDescription = "Latest news, tips, and product updates.", RobotsDirective = "index, follow" },
            new() { PageUrl = "/auth/login", Title = "Sign In – ChatPortal", MetaDescription = "Sign in to your ChatPortal account to access your workspaces and conversations.", MetaKeywords = "login, sign in, account", OgTitle = "Sign In to ChatPortal", OgDescription = "Access your workspaces and conversations.", RobotsDirective = "noindex, follow" },
            new() { PageUrl = "/auth/register", Title = "Create Account – ChatPortal", MetaDescription = "Join ChatPortal and start collaborating with AI-powered chat and analytics.", MetaKeywords = "register, sign up, create account", OgTitle = "Create a ChatPortal Account", OgDescription = "Start collaborating with AI-powered chat and analytics.", RobotsDirective = "noindex, follow" },
            new() { PageUrl = "/dashboard", Title = "Dashboard – ChatPortal", MetaDescription = "Your analytics dashboard with charts, data sources, and workspace insights.", MetaKeywords = "dashboard, analytics, charts, insights", OgTitle = "ChatPortal Dashboard", OgDescription = "Analytics dashboard with charts and insights.", RobotsDirective = "noindex, nofollow" },
            new() { PageUrl = "/chat", Title = "Chat – ChatPortal", MetaDescription = "AI-powered chat workspace for real-time team collaboration.", MetaKeywords = "chat, AI, workspace, collaboration", OgTitle = "ChatPortal Chat", OgDescription = "AI-powered real-time team collaboration.", RobotsDirective = "noindex, nofollow" },
        };

        var toAdd = defaults.Where(d => !existing.Contains(d.PageUrl)).ToList();
        foreach (var entry in toAdd)
        {
            entry.CreatedAt = DateTime.UtcNow;
            entry.LastModified = DateTime.UtcNow;
        }

        _db.SeoEntries.AddRange(toAdd);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, added = toAdd.Count });
    }

    // Bulk submit ALL SEO entry URLs (that are indexable) to IndexNow in a single request.
    // IndexNow protocol: https://www.indexnow.org/documentation
    [HttpPost("/superadmin/seo/indexnow-bulk")]
    public async Task<IActionResult> IndexNowBulk()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var key = config["Seo:IndexNowKey"];
        var keyLocation = config["Seo:IndexNowKeyLocation"];
        var baseUrl = (config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');

        if (string.IsNullOrWhiteSpace(key))
            return BadRequest(new { error = "IndexNow key is not configured (Seo:IndexNowKey)." });

        var host = new Uri(baseUrl).Host;

        // Only submit entries that allow indexing.
        var entries = await _db.SeoEntries
            .Where(e => e.RobotsDirective == null || !e.RobotsDirective.Contains("noindex"))
            .Select(e => e.PageUrl)
            .ToListAsync();

        var urlList = entries
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? p
                : $"{baseUrl}/{p!.TrimStart('/')}")
            .Distinct()
            .ToList();

        if (urlList.Count == 0)
            return Ok(new { success = true, submitted = 0, message = "No indexable URLs to submit." });

        var payload = new
        {
            host,
            key,
            keyLocation = string.IsNullOrWhiteSpace(keyLocation) ? null : keyLocation,
            urlList
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync("https://api.indexnow.org/IndexNow", content);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                return StatusCode((int)resp.StatusCode, new
                {
                    success = false,
                    submitted = urlList.Count,
                    status = (int)resp.StatusCode,
                    error = string.IsNullOrWhiteSpace(body) ? resp.ReasonPhrase : body
                });
            }

            return Ok(new { success = true, submitted = urlList.Count, status = (int)resp.StatusCode });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    [HttpPost("/superadmin/seo/ai-suggest")]
    public async Task<IActionResult> AiSuggestSeo([FromBody] AiSuggestRequest request)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        if (string.IsNullOrWhiteSpace(request.PageUrl))
            return BadRequest(new { error = "Page URL is required." });

        var prompt = $@"You are an SEO expert. Generate optimized SEO metadata for a web page.
Page URL: {request.PageUrl}
Page context: {(string.IsNullOrWhiteSpace(request.Context) ? "AI-powered team collaboration and analytics platform called ChatPortal" : request.Context)}

Respond ONLY with valid JSON (no markdown, no code fences) in this exact format:
{{
  ""title"": ""Page title (50-60 chars)"",
  ""metaDescription"": ""Meta description (150-160 chars)"",
  ""metaKeywords"": ""comma, separated, keywords"",
  ""ogTitle"": ""Open Graph title"",
  ""ogDescription"": ""Open Graph description (under 200 chars)"",
  ""robotsDirective"": ""index, follow""
}}";

        var sb = new StringBuilder();
        await foreach (var chunk in _cohere.StreamChatAsync(prompt, [], "You are an SEO metadata generator. Return only valid JSON."))
        {
            sb.Append(chunk);
        }

        var raw = sb.ToString().Trim();
        // Strip markdown code fences if present
        if (raw.StartsWith("```"))
        {
            var firstNewline = raw.IndexOf('\n');
            var lastFence = raw.LastIndexOf("```");
            if (firstNewline > 0 && lastFence > firstNewline)
                raw = raw[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(raw);
            return Ok(result);
        }
        catch
        {
            return Ok(new { title = "", metaDescription = raw, metaKeywords = "", ogTitle = "", ogDescription = "", robotsDirective = "index, follow" });
        }
    }

    public class AiSuggestRequest
    {
        public string PageUrl { get; set; } = "";
        public string Context { get; set; } = "";
    }

    // ──── About ────
    [HttpGet("/superadmin/about")]
    public async Task<IActionResult> About()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        return View("~/Views/Admin/About.cshtml");
    }

    // ──── Documentation CRUD ────
    [HttpGet("/superadmin/docs")]
    public async Task<IActionResult> Docs()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var docs = await _db.DocArticles.OrderBy(d => d.SortOrder).ThenByDescending(d => d.CreatedAt).ToListAsync();
        return View("~/Views/Admin/Docs.cshtml", docs);
    }

    [HttpPost("/api/superadmin/docs/save")]
    public async Task<IActionResult> SaveDoc([FromBody] SaveDocDto dto)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (dto == null) return BadRequest(new { error = "Body required." });

        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { error = "Title is required." });

        var slug = string.IsNullOrWhiteSpace(dto.Slug)
            ? dto.Title.ToLower().Replace(" ", "-").Replace("--", "-")
            : dto.Slug;

        string? oldUrl = null;
        DocArticle doc;
        if (dto.Id > 0)
        {
            var existing = await _db.DocArticles.FindAsync(dto.Id);
            if (existing == null) return NotFound();
            oldUrl = $"/docs/{existing.Slug}";
            existing.Title = dto.Title;
            existing.Slug = slug;
            existing.Summary = dto.Summary;
            existing.Content = dto.Content;
            existing.Author = dto.Author;
            existing.SortOrder = dto.SortOrder;
            existing.IsPublished = dto.IsPublished;
            existing.UpdatedAt = DateTime.UtcNow;
            doc = existing;
        }
        else
        {
            doc = new DocArticle
            {
                Title = dto.Title,
                Slug = slug,
                Summary = dto.Summary,
                Content = dto.Content,
                Author = dto.Author,
                SortOrder = dto.SortOrder,
                IsPublished = dto.IsPublished,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.DocArticles.Add(doc);
        }

        await _db.SaveChangesAsync();

        var description = !string.IsNullOrWhiteSpace(dto.MetaDescription)
            ? dto.MetaDescription!.Trim()
            : (doc.Summary ?? doc.Title);
        var keywords = !string.IsNullOrWhiteSpace(dto.MetaKeywords)
            ? dto.MetaKeywords!.Trim()
            : "AIInsights365, AI analytics, documentation, " + doc.Slug.Replace('-', ' ');

        await UpsertSeoForContentAsync(
            newUrl: $"/docs/{doc.Slug}",
            oldUrl: oldUrl,
            title: $"{doc.Title} — AIInsights365.net",
            description: description,
            keywords: keywords,
            priority: 0.7m,
            changeFreq: "monthly",
            includeInSitemap: doc.IsPublished,
            ogImage: null);
        return Ok(new { success = true });
    }

    public class SaveDocDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? Content { get; set; }
        public string? Author { get; set; }
        public int SortOrder { get; set; }
        public bool IsPublished { get; set; }
        // SEO overrides (optional — typically supplied by the AI SEO Assistant)
        public string? MetaDescription { get; set; }
        public string? MetaKeywords { get; set; }
    }

    [HttpDelete("/api/superadmin/docs/{id}")]
    public async Task<IActionResult> DeleteDoc(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var doc = await _db.DocArticles.FindAsync(id);
        if (doc == null) return NotFound();
        var url = $"/docs/{doc.Slug}";
        _db.DocArticles.Remove(doc);
        await _db.SaveChangesAsync();
        await RemoveSeoByUrlAsync(url);
        return Ok(new { success = true });
    }

    // ──── Blog CRUD ────
    [HttpGet("/superadmin/blog")]
    public async Task<IActionResult> Blog()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var posts = await _db.BlogPosts.OrderByDescending(b => b.PublishedAt).ToListAsync();
        return View("~/Views/Admin/Blog.cshtml", posts);
    }

    [HttpPost("/api/superadmin/blog/save")]
    public async Task<IActionResult> SaveBlog([FromBody] SaveBlogDto dto)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (dto == null) return BadRequest(new { error = "Body required." });

        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { error = "Title is required." });

        var slug = string.IsNullOrWhiteSpace(dto.Slug)
            ? dto.Title.ToLower().Replace(" ", "-").Replace("--", "-")
            : dto.Slug;

        string? oldUrl = null;
        BlogPost post;
        if (dto.Id > 0)
        {
            var existing = await _db.BlogPosts.FindAsync(dto.Id);
            if (existing == null) return NotFound();
            oldUrl = $"/blog/{existing.Slug}";
            existing.Title = dto.Title;
            existing.Slug = slug;
            existing.Summary = dto.Summary;
            existing.Content = dto.Content;
            existing.Author = dto.Author;
            existing.ImageUrl = dto.ImageUrl;
            if (!existing.IsPublished && dto.IsPublished)
                existing.PublishedAt = DateTime.UtcNow;
            existing.IsPublished = dto.IsPublished;
            post = existing;
        }
        else
        {
            post = new BlogPost
            {
                Title = dto.Title,
                Slug = slug,
                Summary = dto.Summary,
                Content = dto.Content,
                Author = dto.Author,
                ImageUrl = dto.ImageUrl,
                IsPublished = dto.IsPublished,
                PublishedAt = DateTime.UtcNow
            };
            _db.BlogPosts.Add(post);
        }

        await _db.SaveChangesAsync();

        var description = !string.IsNullOrWhiteSpace(dto.MetaDescription)
            ? dto.MetaDescription!.Trim()
            : (post.Summary ?? post.Title);
        var keywords = !string.IsNullOrWhiteSpace(dto.MetaKeywords)
            ? dto.MetaKeywords!.Trim()
            : "AIInsights365, blog, AI analytics, " + post.Slug.Replace('-', ' ');
        var ogImage = !string.IsNullOrWhiteSpace(dto.OgImage)
            ? dto.OgImage!.Trim()
            : (string.IsNullOrWhiteSpace(post.ImageUrl) ? null : post.ImageUrl);

        await UpsertSeoForContentAsync(
            newUrl: $"/blog/{post.Slug}",
            oldUrl: oldUrl,
            title: $"{post.Title} — AIInsights365.net",
            description: description,
            keywords: keywords,
            priority: 0.8m,
            changeFreq: "weekly",
            includeInSitemap: post.IsPublished,
            ogImage: ogImage);
        return Ok(new { success = true });
    }

    public class SaveBlogDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? Content { get; set; }
        public string? Author { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsPublished { get; set; }
        // SEO overrides (optional — typically supplied by the AI SEO Assistant)
        public string? MetaDescription { get; set; }
        public string? MetaKeywords { get; set; }
        public string? OgImage { get; set; }
    }

    [HttpDelete("/api/superadmin/blog/{id}")]
    public async Task<IActionResult> DeleteBlog(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var post = await _db.BlogPosts.FindAsync(id);
        if (post == null) return NotFound();
        var url = $"/blog/{post.Slug}";
        _db.BlogPosts.Remove(post);
        await _db.SaveChangesAsync();
        await RemoveSeoByUrlAsync(url);
        return Ok(new { success = true });
    }

    // ──── AI content generation (Blog & Docs SEO assistant) ────
    public class AiContentRequest
    {
        public string Kind { get; set; } = "blog"; // "blog" or "doc"
        public string Title { get; set; } = "";
        public string? Topic { get; set; }
        public string? Keywords { get; set; }
        public string Mode { get; set; } = "all"; // all|slug|summary|keywords|content
    }

    [HttpPost("/api/superadmin/ai/generate-content")]
    public async Task<IActionResult> GenerateAiContent([FromBody] AiContentRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (req == null) return BadRequest(new { error = "Missing payload." });

        var title = (req.Title ?? "").Trim();
        var topic = (req.Topic ?? "").Trim();
        var seedKeywords = (req.Keywords ?? "").Trim();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(topic))
            return BadRequest(new { error = "Provide a title or a topic to generate content." });

        var kind = string.Equals(req.Kind, "doc", StringComparison.OrdinalIgnoreCase) ? "doc" : "blog";
        var contentTypeLabel = kind == "doc" ? "documentation article" : "blog post";

        var system =
            "You are an expert SEO content strategist and technical writer for AIInsights365, " +
            "an AI-powered analytics platform (chat-with-your-data, AI agents, dashboards, " +
            "Power BI / SQL / REST connectors). You produce content that follows on-page SEO " +
            "best practices: a single focused primary keyword, semantic LSI keywords, " +
            "scannable HTML structure (H2/H3, short paragraphs, bullet lists), natural " +
            "keyword density (~1-2%), descriptive subheadings, and an FAQ section. " +
            "Always respond with a SINGLE valid JSON object only — no markdown fences, " +
            "no commentary, no leading or trailing text.";

        var userPrompt = new StringBuilder();
        userPrompt.AppendLine($"Generate SEO assets for a new {contentTypeLabel} on AIInsights365.net.");
        if (!string.IsNullOrEmpty(title)) userPrompt.AppendLine($"Working title: \"{title}\"");
        if (!string.IsNullOrEmpty(topic)) userPrompt.AppendLine($"Topic / angle: {topic}");
        if (!string.IsNullOrEmpty(seedKeywords)) userPrompt.AppendLine($"Seed keywords (use and expand): {seedKeywords}");
        userPrompt.AppendLine();
        userPrompt.AppendLine("Return a JSON object with EXACTLY these string keys:");
        userPrompt.AppendLine("- \"title\": SEO-optimised title, 50-60 chars, primary keyword near the start.");
        userPrompt.AppendLine("- \"slug\": lowercase URL slug, hyphenated, 3-6 words, ASCII only, no stop words.");
        userPrompt.AppendLine("- \"summary\": 140-180 char card summary, compelling, includes primary keyword.");
        userPrompt.AppendLine("- \"metaDescription\": 150-160 char meta description, action-oriented, primary keyword.");
        userPrompt.AppendLine("- \"metaKeywords\": 8-12 comma-separated SEO keywords (primary + LSI variations).");
        userPrompt.AppendLine("- \"content\": ~2000-word HTML body. Use <h2>, <h3>, <p>, <ul>, <li>, <strong>, <em>, <a>. " +
                              "Do NOT include <h1>, <html>, <head>, or <body> — the title is rendered separately. " +
                              "Open with a hook paragraph, then 5-8 <h2> sections (with <h3> sub-points where useful), " +
                              "include a bulleted list, an FAQ block of 3 questions as <h3>+<p>, and a conclusion <h2>. " +
                              "Use the primary keyword naturally. Do not keyword-stuff.");
        userPrompt.AppendLine();
        userPrompt.AppendLine("HARD REQUIREMENT: the \"content\" field MUST be at least 1900 words of plain readable copy " +
                              "(excluding HTML tags). If shorter, expand sections until it meets the threshold.");

        var sb = new StringBuilder();
        try
        {
            await foreach (var chunk in _cohere.StreamChatAsync(userPrompt.ToString(), new List<(string, string)>(), system))
            {
                sb.Append(chunk);
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "AI generation failed: " + ex.Message });
        }

        var raw = sb.ToString().Trim();
        // Strip optional ```json … ``` fences the model may emit despite instructions.
        if (raw.StartsWith("```"))
        {
            var firstNl = raw.IndexOf('\n');
            if (firstNl >= 0) raw = raw[(firstNl + 1)..];
            if (raw.EndsWith("```")) raw = raw[..^3];
            raw = raw.Trim();
        }

        Dictionary<string, string>? parsed = null;
        try
        {
            parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(raw);
        }
        catch { /* fall through */ }

        if (parsed == null)
            return StatusCode(502, new { error = "AI returned non-JSON content. Try again.", raw });

        string Get(string k) => parsed.TryGetValue(k, out var v) ? (v ?? "") : "";
        var content = Get("content");
        var wordCount = CountPlainTextWords(content);

        return Ok(new
        {
            success = true,
            title = Get("title"),
            slug = Get("slug"),
            summary = Get("summary"),
            metaDescription = Get("metaDescription"),
            metaKeywords = Get("metaKeywords"),
            content,
            wordCount,
            meetsLengthTarget = wordCount >= 1900
        });
    }

    private static int CountPlainTextWords(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length;
    }

    // ── SEO helpers for content CRUD ──────────────────────────
    private async Task UpsertSeoForContentAsync(string newUrl, string? oldUrl, string title,
        string description, string keywords, decimal priority, string changeFreq, bool includeInSitemap,
        string? ogImage = null)
    {
        // If the slug changed, drop the SEO row for the old URL so sitemap stays clean.
        if (!string.IsNullOrEmpty(oldUrl) && !string.Equals(oldUrl, newUrl, StringComparison.OrdinalIgnoreCase))
        {
            await RemoveSeoByUrlAsync(oldUrl);
        }

        var entry = await _db.SeoEntries.FirstOrDefaultAsync(s => s.PageUrl == newUrl);
        if (entry == null)
        {
            _db.SeoEntries.Add(new SeoEntry
            {
                PageUrl = newUrl,
                Title = title,
                MetaDescription = description,
                MetaKeywords = keywords,
                OgTitle = title,
                OgDescription = description,
                OgImage = ogImage,
                SitemapPriority = priority,
                SitemapChangeFreq = changeFreq,
                IncludeInSitemap = includeInSitemap,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            });
        }
        else
        {
            entry.Title = title;
            entry.MetaDescription = description;
            entry.MetaKeywords = keywords;
            entry.OgTitle = title;
            entry.OgDescription = description;
            if (ogImage != null) entry.OgImage = ogImage;
            entry.IncludeInSitemap = includeInSitemap;
            entry.LastModified = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    private async Task RemoveSeoByUrlAsync(string url)
    {
        var entry = await _db.SeoEntries.FirstOrDefaultAsync(s => s.PageUrl == url);
        if (entry != null)
        {
            _db.SeoEntries.Remove(entry);
            await _db.SaveChangesAsync();
        }
    }

    // ── Bulk SEO backfill ─────────────────────────────────────────────────────
    // Pushes meaningful MetaKeywords + MetaDescription onto every:
    //   • static page (only when those fields are currently empty — preserves manual edits)
    //   • BlogPost   (/blog/{slug})
    //   • DocArticle (/docs/{slug})
    // Wrapped in a single transaction so any failure rolls everything back.
    [HttpPost("/api/superadmin/seo/backfill")]
    public async Task<IActionResult> BackfillSeo()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var now = DateTime.UtcNow;
        int staticCreated = 0, staticUpdated = 0, blogCount = 0, docCount = 0;

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // -- 1) Static pages
            var staticPages = new (string Url, string Title, string Description, string Keywords, decimal Priority, string ChangeFreq, string Robots)[]
            {
                ("/",           "AIInsights365 — AI-Powered Data Conversations",
                                "Chat with your data, build dashboards, and connect SQL, Power BI, REST APIs and files with AI agents.",
                                "AI analytics, chat with data, AI agents, dashboards, business intelligence, AIInsights365, data visualization, AI BI platform",
                                1.0m, "daily",   "index, follow"),
                ("/about",      "About — AIInsights365",
                                "Learn how AIInsights365 helps teams turn data into decisions with AI-powered chat, agents, and dashboards.",
                                "about AIInsights365, AI analytics company, AI BI mission, data analytics platform, team",
                                0.6m, "monthly", "index, follow"),
                ("/pricing",    "Pricing — AIInsights365 Plans & Free Trial",
                                "Compare Free, Professional, and Enterprise plans. Start a 30-day free trial with full AI analytics features.",
                                "AIInsights365 pricing, AI analytics pricing, BI subscription, professional plan, enterprise plan, free trial",
                                0.9m, "weekly",  "index, follow"),
                ("/docs",       "Documentation — AIInsights365",
                                "Guides, API references, and tutorials for connecting datasources, building agents, and creating dashboards.",
                                "AIInsights365 docs, documentation, AI agents guide, datasource setup, Power BI connector, SQL connector",
                                0.7m, "weekly",  "index, follow"),
                ("/blog",       "Blog — AIInsights365",
                                "Latest news, AI analytics tutorials, and product updates from the AIInsights365 team.",
                                "AIInsights365 blog, AI analytics blog, BI tutorials, AI agents, dashboards, product updates",
                                0.6m, "daily",   "index, follow"),
                ("/terms",      "Terms & Conditions — AIInsights365",
                                "Terms of use, billing, cancellation and no-refund policy for the AIInsights365 platform.",
                                "AIInsights365 terms, terms of service, billing, cancellation, refund policy",
                                0.3m, "yearly",  "index, follow"),
                ("/sla",        "Service Level Agreement (SLA) — AIInsights365",
                                "Uptime guarantees, support response times, and service commitments for AIInsights365 customers.",
                                "AIInsights365 SLA, uptime, service level agreement, support response time, availability",
                                0.3m, "yearly",  "index, follow"),
                ("/support",    "Support — AIInsights365",
                                "Get help with AIInsights365: documentation, contact support, ticketing, and FAQs.",
                                "AIInsights365 support, contact support, help center, FAQ, customer service, technical support",
                                0.5m, "monthly", "index, follow"),
                ("/auth/login", "Sign In — AIInsights365",
                                "Sign in to AIInsights365 to access your workspaces, agents, and analytics dashboards.",
                                "AIInsights365 login, sign in, account access, workspace login",
                                0.3m, "yearly",  "noindex, follow"),
                ("/auth/register", "Create Account — AIInsights365",
                                "Create a free AIInsights365 account and start chatting with your data using AI agents.",
                                "AIInsights365 register, sign up, create account, free trial, AI analytics signup",
                                0.4m, "monthly", "noindex, follow"),
            };

            foreach (var p in staticPages)
            {
                var entry = await _db.SeoEntries.FirstOrDefaultAsync(s => s.PageUrl == p.Url);
                if (entry == null)
                {
                    _db.SeoEntries.Add(new SeoEntry
                    {
                        PageUrl = p.Url,
                        Title = p.Title,
                        MetaDescription = p.Description,
                        MetaKeywords = p.Keywords,
                        OgTitle = p.Title,
                        OgDescription = p.Description,
                        RobotsDirective = p.Robots,
                        SitemapPriority = p.Priority,
                        SitemapChangeFreq = p.ChangeFreq,
                        IncludeInSitemap = !p.Robots.Contains("noindex"),
                        CreatedBy = "system",
                        CreatedAt = now,
                        LastModified = now
                    });
                    staticCreated++;
                }
                else
                {
                    var changed = false;
                    if (string.IsNullOrWhiteSpace(entry.MetaKeywords))     { entry.MetaKeywords     = p.Keywords;    changed = true; }
                    if (string.IsNullOrWhiteSpace(entry.MetaDescription))  { entry.MetaDescription  = p.Description; changed = true; }
                    if (string.IsNullOrWhiteSpace(entry.OgDescription))    { entry.OgDescription    = p.Description; changed = true; }
                    if (string.IsNullOrWhiteSpace(entry.OgTitle))          { entry.OgTitle          = p.Title;       changed = true; }
                    if (string.IsNullOrWhiteSpace(entry.Title))            { entry.Title            = p.Title;       changed = true; }
                    if (string.IsNullOrWhiteSpace(entry.RobotsDirective))  { entry.RobotsDirective  = p.Robots;      changed = true; }
                    if (changed) { entry.LastModified = now; staticUpdated++; }
                }
            }
            await _db.SaveChangesAsync();

            // -- 2) Blog posts
            var posts = await _db.BlogPosts.AsNoTracking().ToListAsync();
            foreach (var post in posts)
            {
                var description = !string.IsNullOrWhiteSpace(post.Summary) ? post.Summary! : post.Title;
                var keywords = BuildKeywordsFor(post.Title, post.Slug,
                    "blog, AI analytics, AIInsights365, data insights");
                await UpsertSeoForContentAsync(
                    newUrl: $"/blog/{post.Slug}",
                    oldUrl: null,
                    title: $"{post.Title} — AIInsights365.net",
                    description: description,
                    keywords: keywords,
                    priority: 0.8m,
                    changeFreq: "weekly",
                    includeInSitemap: post.IsPublished,
                    ogImage: string.IsNullOrWhiteSpace(post.ImageUrl) ? null : post.ImageUrl);
                blogCount++;
            }

            // -- 3) Doc articles
            var docs = await _db.DocArticles.AsNoTracking().ToListAsync();
            foreach (var doc in docs)
            {
                var description = !string.IsNullOrWhiteSpace(doc.Summary) ? doc.Summary! : doc.Title;
                var keywords = BuildKeywordsFor(doc.Title, doc.Slug,
                    "documentation, AIInsights365 docs, AI analytics guide, tutorial");
                await UpsertSeoForContentAsync(
                    newUrl: $"/docs/{doc.Slug}",
                    oldUrl: null,
                    title: $"{doc.Title} — AIInsights365.net",
                    description: description,
                    keywords: keywords,
                    priority: 0.7m,
                    changeFreq: "monthly",
                    includeInSitemap: doc.IsPublished,
                    ogImage: null);
                docCount++;
            }

            _db.ActivityLogs.Add(new ActivityLog
            {
                Action = "Seo.Backfill",
                Description = $"Backfilled SEO keywords. staticCreated={staticCreated}, staticUpdated={staticUpdated}, blogs={blogCount}, docs={docCount}.",
                UserId = GetCurrentUserId() ?? "",
                CreatedAt = now
            });
            await _db.SaveChangesAsync();

            // Read-back sanity check before commit so the UI can confirm rows exist.
            var totalSeoRows = await _db.SeoEntries.CountAsync();

            await tx.CommitAsync();

            return Ok(new
            {
                success = true,
                staticCreated,
                staticUpdated,
                blogs = blogCount,
                docs = docCount,
                total = staticCreated + staticUpdated + blogCount + docCount,
                totalSeoRows
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new
            {
                success = false,
                error = "Backfill failed; no changes were saved.",
                detail = ex.Message,
                inner = ex.InnerException?.Message
            });
        }
    }

    // Lightweight, deterministic keyword builder: picks meaningful words from the title
    // and slug, drops English stop-words and short tokens, and merges with topic stems.
    private static string BuildKeywordsFor(string title, string slug, string topicStems)
    {
        static IEnumerable<string> Tokenize(string s) =>
            (s ?? "")
                .Replace('-', ' ').Replace('_', ' ').Replace('/', ' ')
                .Split(new[] { ' ', '\t', ',', '.', ':', ';', '!', '?', '(', ')', '[', ']', '"', '\'' },
                       StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim().ToLowerInvariant());

        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the","a","an","and","or","but","of","to","in","on","for","with","by","is","are",
            "be","as","at","from","that","this","it","its","into","your","you","we","our","my",
            "how","what","why","when","where","which","who","do","does","can","will","vs","via","using"
        };

        var tokens = Tokenize(title).Concat(Tokenize(slug))
            .Where(w => w.Length >= 3 && !stop.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        var stemList = (topicStems ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return string.Join(", ",
            tokens.Concat(stemList)
                  .Where(s => !string.IsNullOrWhiteSpace(s))
                  .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    // ── Notifications ─────────────────────────────────────────────────────────
    [HttpGet("/superadmin/notifications")]
    public async Task<IActionResult> Notifications()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var items = await _db.Notifications
            .OrderByDescending(n => n.CreatedAt)
            .Take(200)
            .ToListAsync();
        var orgs = await _db.Organizations
            .OrderBy(o => o.Name)
            .Select(o => new { o.Id, o.Name })
            .ToListAsync();
        var templates = await _db.NotificationTemplates
            .OrderBy(t => t.Name)
            .ToListAsync();
        ViewBag.Orgs = orgs;
        ViewBag.Templates = templates;
        return View("~/Views/Admin/Notifications.cshtml", items);
    }

    [HttpPost("/api/superadmin/notifications/broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] BroadcastNotificationDto dto)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (dto == null || string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Body))
            return BadRequest(new { error = "Title and body are required." });

        var callerId = GetCurrentUserId();
        var scope = string.IsNullOrWhiteSpace(dto.Scope) ? "All" : dto.Scope!.Trim();
        var now = DateTime.UtcNow;
        var severity = string.IsNullOrWhiteSpace(dto.Severity) ? "normal" : dto.Severity!.Trim();
        var isScheduled = dto.ScheduleAt.HasValue && dto.ScheduleAt.Value > now;

        // Validate scope-specific parameters
        if (scope.Equals("SpecificOrgs", StringComparison.OrdinalIgnoreCase) || scope.Equals("Org", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.OrganizationIds == null || dto.OrganizationIds.Count == 0)
                return BadRequest(new { error = "At least one organization must be selected." });
        }
        else if (scope.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.UserIds == null || dto.UserIds.Count == 0)
                return BadRequest(new { error = "At least one user ID must be provided." });
        }
        else if (scope.Equals("Role", StringComparison.OrdinalIgnoreCase))
        {
            if (dto.Roles == null || dto.Roles.Count == 0)
                return BadRequest(new { error = "At least one role must be provided." });
        }

        var notifications = new List<Notification>();

        if (scope.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            notifications.Add(new Notification
            {
                Scope = "All",
                Title = dto.Title!.Trim(),
                Body = dto.Body!.Trim(),
                Type = string.IsNullOrWhiteSpace(dto.Type) ? "Announcement" : dto.Type!.Trim(),
                Severity = severity,
                Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link!.Trim(),
                ExpiresAt = dto.ExpiresAt,
                CreatedByUserId = callerId,
                CreatedByRole = "SuperAdmin",
                CreatedAt = now,
                ScheduleAt = dto.ScheduleAt,
                DeliveryStatus = isScheduled ? "Scheduled" : "Delivered",
                DeliveredAt = isScheduled ? null : now
            });
        }
        else if (scope.Equals("SpecificOrgs", StringComparison.OrdinalIgnoreCase) || scope.Equals("Org", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var orgId in dto.OrganizationIds!.Distinct())
            {
                notifications.Add(new Notification
                {
                    Scope = "Org",
                    OrganizationId = orgId,
                    Title = dto.Title!.Trim(),
                    Body = dto.Body!.Trim(),
                    Type = string.IsNullOrWhiteSpace(dto.Type) ? "Announcement" : dto.Type!.Trim(),
                    Severity = severity,
                    Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link!.Trim(),
                    ExpiresAt = dto.ExpiresAt,
                    CreatedByUserId = callerId,
                    CreatedByRole = "SuperAdmin",
                    CreatedAt = now,
                    ScheduleAt = dto.ScheduleAt,
                    DeliveryStatus = isScheduled ? "Scheduled" : "Delivered",
                    DeliveredAt = isScheduled ? null : now
                });
            }
        }
        else if (scope.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            notifications.Add(new Notification
            {
                Scope = "User",
                TargetUserIdsCsv = string.Join(",", dto.UserIds!.Select(id => id.Trim()).Distinct()),
                Title = dto.Title!.Trim(),
                Body = dto.Body!.Trim(),
                Type = string.IsNullOrWhiteSpace(dto.Type) ? "Announcement" : dto.Type!.Trim(),
                Severity = severity,
                Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link!.Trim(),
                ExpiresAt = dto.ExpiresAt,
                CreatedByUserId = callerId,
                CreatedByRole = "SuperAdmin",
                CreatedAt = now,
                ScheduleAt = dto.ScheduleAt,
                DeliveryStatus = isScheduled ? "Scheduled" : "Delivered",
                DeliveredAt = isScheduled ? null : now
            });
        }
        else if (scope.Equals("Role", StringComparison.OrdinalIgnoreCase))
        {
            notifications.Add(new Notification
            {
                Scope = "Role",
                TargetRolesCsv = string.Join(",", dto.Roles!.Select(r => r.Trim()).Distinct()),
                Title = dto.Title!.Trim(),
                Body = dto.Body!.Trim(),
                Type = string.IsNullOrWhiteSpace(dto.Type) ? "Announcement" : dto.Type!.Trim(),
                Severity = severity,
                Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link!.Trim(),
                ExpiresAt = dto.ExpiresAt,
                CreatedByUserId = callerId,
                CreatedByRole = "SuperAdmin",
                CreatedAt = now,
                ScheduleAt = dto.ScheduleAt,
                DeliveryStatus = isScheduled ? "Scheduled" : "Delivered",
                DeliveredAt = isScheduled ? null : now
            });
        }
        else
        {
            return BadRequest(new { error = $"Unknown scope '{scope}'." });
        }

        _db.Notifications.AddRange(notifications);
        await _db.SaveChangesAsync();

        // Log activity
        foreach (var n in notifications)
        {
            _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
            {
                Action = "Notification.Broadcast",
                Description = $"Broadcast '{n.Title}' scope={n.Scope} status={n.DeliveryStatus}",
                UserId = callerId ?? "",
                CreatedAt = now
            });
        }
        await _db.SaveChangesAsync();

        // Fan out UserNotification rows immediately if not scheduled
        if (!isScheduled)
        {
            var emailer = HttpContext.RequestServices.GetService<AIInsights.SuperAdmin.Services.IUrgentNotificationEmailer>();
            var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            foreach (var n in notifications)
            {
                await FanOutAsync(n, emailer, config);
            }
        }

        return Ok(new { success = true, count = notifications.Count, scheduled = isScheduled });
    }

    /// <summary>Resolve recipients and create UserNotification rows (for User/Role scoped notifications).</summary>
    private async Task FanOutAsync(Notification notification,
        AIInsights.SuperAdmin.Services.IUrgentNotificationEmailer? emailer,
        IConfiguration config)
    {
        // Fan-out for ALL scopes so metrics (UserNotification rows) are accurate
        var recipients = await AIInsights.SuperAdmin.Services.NotificationDispatcher
            .ResolveRecipientsAsync(_db, notification, CancellationToken.None);

        var distinctIds = recipients.Select(r => r.Id).Distinct().ToHashSet();
        if (distinctIds.Count == 0) return;

        var existingUserIds = await _db.UserNotifications
            .Where(un => un.NotificationId == notification.Id && distinctIds.Contains(un.UserId))
            .Select(un => un.UserId)
            .ToListAsync();
        var existingSet = existingUserIds.ToHashSet();

        var isUrgent = string.Equals(notification.Severity, "urgent", StringComparison.OrdinalIgnoreCase);
        var baseUrl = config["AppBaseUrl"] ?? "";

        var newRows = new List<AIInsights.Models.UserNotification>();
        foreach (var uid in distinctIds)
        {
            if (existingSet.Contains(uid)) continue;
            newRows.Add(new AIInsights.Models.UserNotification
            {
                UserId = uid,
                NotificationId = notification.Id
            });
        }

        if (newRows.Count > 0)
        {
            _db.UserNotifications.AddRange(newRows);
            await _db.SaveChangesAsync();

            if (isUrgent && emailer != null)
            {
                var recipientMap = recipients.ToDictionary(r => r.Id);
                var rowData = newRows.Select(r => new { r.Id, r.UserId }).ToList();
                var notificationTitle = notification.Title;
                var notificationBody = notification.Body;
                var scopeFactory = _scopeFactory;

                _ = Task.Run(async () =>
                {
                    foreach (var rd in rowData)
                    {
                        var user = recipientMap.TryGetValue(rd.UserId, out var u) ? u : null;
                        var email = user?.Email ?? "";
                        var name = user?.FullName ?? "";
                        if (string.IsNullOrWhiteSpace(email)) continue;

                        var clickUrl = $"{baseUrl}/n/{rd.Id}/click";
                        try
                        {
                            var sent = await emailer.SendAsync(email, name,
                                notificationTitle, notificationBody, clickUrl, CancellationToken.None);
                            if (sent)
                            {
                                using var scope = scopeFactory.CreateScope();
                                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                var un = await scopedDb.UserNotifications.FindAsync(rd.Id);
                                if (un != null)
                                {
                                    un.EmailSent = true;
                                    await scopedDb.SaveChangesAsync(CancellationToken.None);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            using var errScope = scopeFactory.CreateScope();
                            var logger = errScope.ServiceProvider
                                .GetRequiredService<ILogger<SuperAdminController>>();
                            logger.LogError(ex, "Failed to send urgent email for UserNotification {Id}.", rd.Id);
                        }
                    }
                }, CancellationToken.None);
            }
        }
    }

    [HttpPost("/api/superadmin/notifications/{id:int}/cancel")]
    public async Task<IActionResult> CancelNotification(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();
        if (n.DeliveryStatus != "Scheduled")
            return BadRequest(new { error = "Only scheduled notifications can be cancelled." });

        n.DeliveryStatus = "Cancelled";
        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Cancel",
            Description = $"Cancelled scheduled notification '{n.Title}' (id={n.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPut("/api/superadmin/notifications/{id:int}")]
    public async Task<IActionResult> EditNotification(int id, [FromBody] EditNotificationDto dto)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (dto == null) return BadRequest(new { error = "Body required." });

        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();
        if (n.DeliveryStatus != "Scheduled")
            return BadRequest(new { error = "Only scheduled notifications can be edited." });

        if (!string.IsNullOrWhiteSpace(dto.Title)) n.Title = dto.Title.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Body)) n.Body = dto.Body.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Type)) n.Type = dto.Type.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Severity)) n.Severity = dto.Severity.Trim();
        if (dto.Link != null) n.Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link.Trim();
        if (dto.ExpiresAt.HasValue) n.ExpiresAt = dto.ExpiresAt;
        if (dto.ScheduleAt.HasValue) n.ScheduleAt = dto.ScheduleAt;
        if (!string.IsNullOrWhiteSpace(dto.Scope)) n.Scope = dto.Scope.Trim();
        if (dto.TargetUserIds != null) n.TargetUserIdsCsv = string.Join(",", dto.TargetUserIds);
        if (dto.TargetRoles != null) n.TargetRolesCsv = string.Join(",", dto.TargetRoles);

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Edit",
            Description = $"Edited scheduled notification '{n.Title}' (id={n.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/superadmin/notifications/{id:int}/recall")]
    public async Task<IActionResult> RecallNotification(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();
        if (n.IsRecalled)
            return BadRequest(new { error = "Notification is already recalled." });
        if (n.DeliveryStatus != "Delivered" && n.DeliveryStatus != "Scheduled")
            return BadRequest(new { error = "Only delivered or scheduled notifications can be recalled." });

        var now = DateTime.UtcNow;
        n.IsRecalled = true;
        n.RecalledAt = now;
        n.RecalledByUserId = GetCurrentUserId();
        // If still scheduled, cancel it so dispatcher won't deliver it
        if (n.DeliveryStatus == "Scheduled")
            n.DeliveryStatus = "Cancelled";

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Recall",
            Description = $"Recalled notification '{n.Title}' (id={n.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = now
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("/api/superadmin/notifications/{id:int}/metrics")]
    public async Task<IActionResult> NotificationMetrics(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();

        var rows = await _db.UserNotifications
            .Where(un => un.NotificationId == id)
            .Select(un => new { un.ReadAt, un.IsClicked, un.EmailSent })
            .ToListAsync();

        var total = rows.Count;
        var read = rows.Count(r => r.ReadAt != null);
        var clicked = rows.Count(r => r.IsClicked);
        var emailed = rows.Count(r => r.EmailSent);

        return Ok(new
        {
            id = n.Id,
            title = n.Title,
            deliveryStatus = n.DeliveryStatus,
            scheduledAt = n.ScheduleAt,
            deliveredAt = n.DeliveredAt,
            totalRecipients = total,
            read,
            unread = total - read,
            clicked,
            emailed,
            readRate = total > 0 ? Math.Round((double)read / total, 4) : 0.0,
            clickRate = total > 0 ? Math.Round((double)clicked / total, 4) : 0.0
        });
    }

    [HttpDelete("/api/superadmin/notifications/{id:int}")]
    public async Task<IActionResult> DeleteNotification(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var n = await _db.Notifications.FindAsync(id);
        if (n == null) return NotFound();

        var callerId = GetCurrentUserId();
        var title = n.Title;

        _db.Notifications.Remove(n);

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Delete",
            Description = $"Deleted notification \"{title}\" (id: {id})",
            UserId = callerId ?? "",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ── Notification Templates ────────────────────────────────────────────────

    [HttpGet("/api/superadmin/notification-templates")]
    public async Task<IActionResult> GetTemplates()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var list = await _db.NotificationTemplates.OrderBy(t => t.Name).ToListAsync();
        return Ok(list);
    }

    [HttpPost("/api/superadmin/notification-templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] NotificationTemplateDto dto)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (dto == null) return BadRequest(new { error = "Request body is required." });

        var name = dto.Name?.Trim() ?? "";
        var title = dto.Title?.Trim() ?? "";
        var body = dto.Body?.Trim() ?? "";
        var type = dto.Type?.Trim() ?? "Announcement";
        var severity = dto.Severity?.Trim() ?? "normal";
        var link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Name is required." });
        if (name.Length > 120)
            return BadRequest(new { error = "Name must be 120 characters or fewer." });
        if (title.Length > 200)
            return BadRequest(new { error = "Title must be 200 characters or fewer." });
        if (type.Length > 40)
            return BadRequest(new { error = "Type must be 40 characters or fewer." });
        if (severity.Length > 20)
            return BadRequest(new { error = "Severity must be 20 characters or fewer." });

        var now = DateTime.UtcNow;
        var tmpl = new AIInsights.Models.NotificationTemplate
        {
            Name = name,
            Title = title,
            Body = body,
            Type = type,
            Severity = severity,
            Link = link,
            CreatedByUserId = GetCurrentUserId(),
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.NotificationTemplates.Add(tmpl);
        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Template.Create",
            Description = $"Created notification template '{tmpl.Name}'",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = now
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true, id = tmpl.Id });
    }

    [HttpPut("/api/superadmin/notification-templates/{id:int}")]
    public async Task<IActionResult> UpdateTemplate(int id, [FromBody] NotificationTemplateDto dto)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        if (dto == null) return BadRequest(new { error = "Body required." });

        var tmpl = await _db.NotificationTemplates.FindAsync(id);
        if (tmpl == null) return NotFound();

        var name = dto.Name?.Trim();
        var title = dto.Title?.Trim();
        var type = dto.Type?.Trim();
        var severity = dto.Severity?.Trim();

        if (name != null && name.Length > 120)
            return BadRequest(new { error = "Name must be 120 characters or fewer." });
        if (title != null && title.Length > 200)
            return BadRequest(new { error = "Title must be 200 characters or fewer." });
        if (type != null && type.Length > 40)
            return BadRequest(new { error = "Type must be 40 characters or fewer." });
        if (severity != null && severity.Length > 20)
            return BadRequest(new { error = "Severity must be 20 characters or fewer." });

        if (!string.IsNullOrWhiteSpace(name)) tmpl.Name = name;
        if (!string.IsNullOrWhiteSpace(title)) tmpl.Title = title;
        if (!string.IsNullOrWhiteSpace(dto.Body)) tmpl.Body = dto.Body.Trim();
        if (!string.IsNullOrWhiteSpace(type)) tmpl.Type = type;
        if (!string.IsNullOrWhiteSpace(severity)) tmpl.Severity = severity;
        if (dto.Link != null) tmpl.Link = string.IsNullOrWhiteSpace(dto.Link) ? null : dto.Link.Trim();
        tmpl.UpdatedAt = DateTime.UtcNow;

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Template.Update",
            Description = $"Updated notification template '{tmpl.Name}' (id={tmpl.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("/api/superadmin/notification-templates/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var tmpl = await _db.NotificationTemplates.FindAsync(id);
        if (tmpl == null) return NotFound();

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Notification.Template.Delete",
            Description = $"Deleted notification template '{tmpl.Name}' (id={tmpl.Id})",
            UserId = GetCurrentUserId() ?? "",
            CreatedAt = DateTime.UtcNow
        });
        _db.NotificationTemplates.Remove(tmpl);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // DTOs
    public class BroadcastNotificationDto
    {
        public string? Scope { get; set; } // "All" | "Org" | "SpecificOrgs" | "User" | "Role"
        public List<int>? OrganizationIds { get; set; }
        public List<string>? UserIds { get; set; }
        public List<string>? Roles { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Type { get; set; }
        public string? Severity { get; set; }
        public string? Link { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? ScheduleAt { get; set; }
    }

    public class EditNotificationDto
    {
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Type { get; set; }
        public string? Severity { get; set; }
        public string? Link { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? ScheduleAt { get; set; }
        public string? Scope { get; set; }
        public List<string>? TargetUserIds { get; set; }
        public List<string>? TargetRoles { get; set; }
    }

    public class NotificationTemplateDto
    {
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Type { get; set; }
        public string? Severity { get; set; }
        public string? Link { get; set; }
    }

    // ──── Payments Tracking ────
    [HttpGet("/superadmin/payments")]
    public async Task<IActionResult> Payments_All([FromQuery] int page = 1, [FromQuery] string? search = null)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        const int pageSize = 50;

        var query = _db.PaymentRecords
            .Include(p => p.Organization)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                (p.Organization != null && p.Organization.Name.ToLower().Contains(term)) ||
                p.PaymentType.ToLower().Contains(term) ||
                p.Status.ToLower().Contains(term) ||
                (p.PayPalOrderId != null && p.PayPalOrderId.ToLower().Contains(term)) ||
                (p.Description != null && p.Description.ToLower().Contains(term)));
        }

        var records = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var totalCount = await query.CountAsync();

        ViewBag.Page = page;
        ViewBag.Search = search;
        ViewBag.TotalCount = totalCount;
        ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return View("~/Views/Admin/Payments.cshtml", records);
    }

    // ──── Block / Unblock Organizations ────
    [HttpGet("/superadmin/org-management")]
    public async Task<IActionResult> OrgManagement()
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);

        var orgs = await _db.Organizations
            .OrderByDescending(o => o.IsBlocked)
            .ThenBy(o => o.Name)
            .ToListAsync();

        return View("~/Views/Admin/OrgManagement.cshtml", orgs);
    }

    [HttpPost("/api/superadmin/orgs/{id}/block")]
    public async Task<IActionResult> BlockOrg(int id, [FromBody] BlockOrgRequest req)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        var callerId = GetCurrentUserId();
        var now = DateTime.UtcNow;

        org.IsBlocked = true;
        org.BlockedReason = req.Reason;
        org.BlockedAt = now;

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Org.Block",
            Description = $"Blocked organization {org.Name}. Reason: {req.Reason ?? "—"}",
            UserId = callerId ?? "",
            OrganizationId = id,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("/api/superadmin/orgs/{id}/unblock")]
    public async Task<IActionResult> UnblockOrg(int id)
    {
        if (!await IsSuperAdminAsync()) return StatusCode(403);
        var org = await _db.Organizations.FindAsync(id);
        if (org == null) return NotFound();

        var callerId = GetCurrentUserId();
        var now = DateTime.UtcNow;

        org.IsBlocked = false;
        org.BlockedReason = null;
        org.BlockedAt = null;

        _db.ActivityLogs.Add(new AIInsights.Models.ActivityLog
        {
            Action = "Org.Unblock",
            Description = $"Unblocked organization {org.Name}.",
            UserId = callerId ?? "",
            OrganizationId = id,
            CreatedAt = now
        });

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    public class BlockOrgRequest
    {
        public string? Reason { get; set; }
    }

    [HttpGet("/superadmin/error")]
    [AllowAnonymous]
    public IActionResult Error()
    {
        return Content("An unexpected error occurred. Please try again later.", "text/plain");
    }
}
