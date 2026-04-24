namespace AIInsights.Models;

/// <summary>
/// Per-user read / dismissed state for a <see cref="Notification"/>.
/// Rows are created lazily the first time a user interacts with (or fetches) the notification.
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
}
