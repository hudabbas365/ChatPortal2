using AIInsights.Data;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace AIInsights.SuperAdmin.Services;

public class BlogAnnouncementEmailQueueService : BackgroundService
{
    private const int BatchSize = 50;
    private const int ErrorMessageMaxLength = 500;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlogAnnouncementEmailQueueService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(20);

    public BlogAnnouncementEmailQueueService(IServiceScopeFactory scopeFactory, ILogger<BlogAnnouncementEmailQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessQueueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Blog announcement queue processing failed.");
            }
        }
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IFeatureAnnouncementEmailSender>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var queued = await db.BlogAnnouncementEmailLogs
            .Where(l => l.Status == "Queued")
            .OrderBy(l => l.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (queued.Count == 0) return;

        var blogIds = queued.Select(q => q.BlogId).Distinct().ToList();
        var blogs = await db.BlogPosts
            .Include(b => b.BlogImages.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
            .Where(b => blogIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        foreach (var log in queued)
        {
            if (!blogs.TryGetValue(log.BlogId, out var blog))
            {
                log.Status = "Failed";
                log.ErrorMessage = "Blog not found.";
                continue;
            }

            var subject = string.IsNullOrWhiteSpace(blog.EmailSubject)
                ? $"Feature update: {blog.Title}"
                : blog.EmailSubject!;

            var body = BuildHtmlTemplate(configuration, blog.Title, blog.Summary, blog.FeaturedImagePath ?? blog.ImageUrl, $"/blog/{blog.Slug}");

            var attempts = 0;
            Exception? lastError = null;
            while (attempts < 3)
            {
                attempts++;
                try
                {
                    await sender.SendAsync(log.SubscriberEmail, subject, body, cancellationToken);
                    log.Status = "Sent";
                    log.ErrorMessage = null;
                    log.SentAt = DateTime.UtcNow;
                    lastError = null;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (!IsTransient(ex) || attempts >= 3) break;
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempts), cancellationToken);
                }
            }

            if (lastError != null)
            {
                log.Status = "Failed";
                log.ErrorMessage = lastError.Message.Length > ErrorMessageMaxLength ? lastError.Message[..ErrorMessageMaxLength] : lastError.Message;
                _logger.LogWarning(lastError, "Feature announcement send failed for BlogId={BlogId}, recipient masked.", log.BlogId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string BuildHtmlTemplate(IConfiguration configuration, string title, string? summary, string? imageUrl, string blogPath)
    {
        var baseUrl = (configuration["App:BaseUrl"] ?? "").TrimEnd('/');
        var blogUrl = string.IsNullOrEmpty(baseUrl) ? blogPath : $"{baseUrl}{blogPath}";
        var unsubscribeUrl = string.IsNullOrEmpty(baseUrl)
            ? "/support?unsubscribe=announcements"
            : $"{baseUrl}/support?unsubscribe=announcements";
        var resolvedImage = imageUrl ?? "";
        if (!string.IsNullOrWhiteSpace(baseUrl) && resolvedImage.StartsWith('/'))
            resolvedImage = $"{baseUrl}{resolvedImage}";
        var safeSummary = System.Net.WebUtility.HtmlEncode(summary ?? "");
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        var safeImage = string.IsNullOrWhiteSpace(resolvedImage) ? "" : $"<img src=\"{resolvedImage}\" alt=\"{safeTitle}\" style=\"max-width:100%;border-radius:10px;margin-bottom:16px;\" />";

        return $"""
            <!doctype html>
            <html>
            <body style="margin:0;padding:16px;background:#f5f7fb;font-family:Arial,Helvetica,sans-serif;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:680px;margin:0 auto;background:#ffffff;border-radius:12px;padding:24px;">
                <tr><td>
                  <h2 style="margin-top:0;color:#1e3a5f;">New feature announcement</h2>
                  <h3 style="margin:8px 0 16px 0;color:#111827;">{safeTitle}</h3>
                  {safeImage}
                  <p style="color:#374151;line-height:1.6;">{safeSummary}</p>
                  <p style="margin:24px 0;">
                    <a href="{blogUrl}" style="display:inline-block;background:#2563eb;color:#fff;text-decoration:none;padding:12px 18px;border-radius:8px;">Read the full announcement</a>
                  </p>
                  <p style="font-size:12px;color:#6b7280;">
                    You received this because your account is subscribed to feature announcements.
                    <a href="{unsubscribeUrl}">Unsubscribe</a>
                  </p>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is SmtpException smtpEx)
            return smtpEx.StatusCode == SmtpStatusCode.GeneralFailure ||
                   smtpEx.StatusCode == SmtpStatusCode.MailboxBusy ||
                   smtpEx.StatusCode == SmtpStatusCode.TransactionFailed;

        return ex is TimeoutException || ex.InnerException is TimeoutException;
    }
}
