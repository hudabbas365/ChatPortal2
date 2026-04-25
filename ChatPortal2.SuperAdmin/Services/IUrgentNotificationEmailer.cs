namespace AIInsights.SuperAdmin.Services;

/// <summary>
/// Sends email notifications for urgent-severity broadcasts.
/// </summary>
public interface IUrgentNotificationEmailer
{
    /// <summary>
    /// Sends an urgent notification email to a single recipient.
    /// Returns true on success, false on failure (failure is logged internally).
    /// </summary>
    Task<bool> SendAsync(string toEmail, string toName, string notificationTitle,
        string notificationBody, string clickUrl, CancellationToken cancellationToken = default);
}
