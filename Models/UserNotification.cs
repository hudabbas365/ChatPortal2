using System.ComponentModel.DataAnnotations.Schema;

namespace AIInsights.Models;

/// <summary>
/// Per-user read / dismissed state for a <see cref="Notification"/>.
/// Rows are created lazily the first time a user interacts with (or fetches) the notification,
/// or eagerly for targeted (User/Role) notifications.
/// </summary>
public class UserNotification
{
    public int Id { get; set; }

    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }

    public int NotificationId { get; set; }
    public Notification? Notification { get; set; }

    public DateTime? ReadAt { get; set; }
    public DateTime? DismissedAt { get; set; }

    // ── Computed read state ──────────────────────────────────────────────────
    [NotMapped]
    public bool IsRead => ReadAt != null;

    // ── Click-through tracking (D10) ─────────────────────────────────────────
    public bool IsClicked { get; set; } = false;
    public DateTime? ClickedAt { get; set; }

    // ── Email fan-out tracking (D13) ─────────────────────────────────────────
    public bool EmailSent { get; set; } = false;
}
