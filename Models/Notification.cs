namespace AIInsights.Models;

/// <summary>
/// A notification message that can be delivered to one user, all users in an organization,
/// or broadcast globally. Per-user read / dismissed state is tracked in <see cref="UserNotification"/>.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>Scope of delivery: "User" | "Org" | "All" | "Role".</summary>
    public string Scope { get; set; } = "Org";

    /// <summary>When <see cref="Scope"/> == "Org", the target organization. Null for global broadcasts.</summary>
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>When <see cref="Scope"/> == "User" (legacy single-user), the target user.</summary>
    public string? TargetUserId { get; set; }

    /// <summary>Comma-separated Identity user IDs for multi-user targeting (Scope="User").</summary>
    public string? TargetUserIdsCsv { get; set; }

    /// <summary>Comma-separated role names for role-based targeting (Scope="Role").</summary>
    public string? TargetRolesCsv { get; set; }

    public string Title { get; set; } = "";
    public string Body { get; set; } = "";

    /// <summary>Category: "Info" | "Warning" | "Success" | "Trial" | "EmailVerify" | "System" | "Announcement".</summary>
    public string Type { get; set; } = "Info";

    /// <summary>"low" | "normal" | "high" | "urgent".</summary>
    public string Severity { get; set; } = "normal";

    /// <summary>Optional click-through link.</summary>
    public string? Link { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    public string? CreatedByUserId { get; set; }

    /// <summary>"SuperAdmin" | "OrgAdmin" | "System".</summary>
    public string? CreatedByRole { get; set; }

    /// <summary>
    /// Stable key used by the background seeder to dedupe system notifications.
    /// e.g. "trial-expiring-org-42", "email-verify-org-42", "trial-expired-org-42".
    /// </summary>
    public string? SystemKey { get; set; }

    // ── Scheduled delivery (D9) ──────────────────────────────────────────────

    /// <summary>When to deliver. Null means deliver immediately.</summary>
    public DateTime? ScheduleAt { get; set; }

    /// <summary>Set when fan-out actually completed.</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>"Pending" | "Scheduled" | "Delivered" | "Cancelled" | "Failed".</summary>
    public string DeliveryStatus { get; set; } = "Delivered";

    // ── Recall (D12) ────────────────────────────────────────────────────────

    /// <summary>When true, the notification is hidden from all user-facing lists.</summary>
    public bool IsRecalled { get; set; } = false;

    public DateTime? RecalledAt { get; set; }
    public string? RecalledByUserId { get; set; }
}
