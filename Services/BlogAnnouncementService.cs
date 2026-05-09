using AIInsights.Data;
using AIInsights.Models;
using Microsoft.EntityFrameworkCore;

namespace AIInsights.Services;

public interface IBlogAnnouncementService
{
    Task<AnnouncementRecipientPreview> PreviewRecipientsAsync(bool sendToAllSubscribers, IEnumerable<int>? subscriptionIds, CancellationToken cancellationToken = default);
    Task<AnnouncementQueueResult> QueueAnnouncementAsync(int blogId, bool resendFailedOnly = false, CancellationToken cancellationToken = default);
    Task<int> ProcessQueuedEmailsAsync(int batchSize = 50, CancellationToken cancellationToken = default);
}

public class AnnouncementRecipientPreview
{
    public int RecipientCount { get; set; }
    public List<string> SampleRecipients { get; set; } = new();
}

public class AnnouncementQueueResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int RecipientCount { get; set; }
}

public class BlogAnnouncementService : IBlogAnnouncementService
{
    private readonly AppDbContext _db;
    private readonly IAnnouncementEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BlogAnnouncementService> _logger;

    public BlogAnnouncementService(
        AppDbContext db,
        IAnnouncementEmailSender emailSender,
        IConfiguration configuration,
        ILogger<BlogAnnouncementService> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AnnouncementRecipientPreview> PreviewRecipientsAsync(bool sendToAllSubscribers, IEnumerable<int>? subscriptionIds, CancellationToken cancellationToken = default)
    {
        var recipients = await ResolveRecipientsAsync(sendToAllSubscribers, subscriptionIds, cancellationToken);
        return new AnnouncementRecipientPreview
        {
            RecipientCount = recipients.Count,
            SampleRecipients = recipients.Select(r => r.Email!).Take(5).ToList()
        };
    }

    public async Task<AnnouncementQueueResult> QueueAnnouncementAsync(int blogId, bool resendFailedOnly = false, CancellationToken cancellationToken = default)
    {
        var blog = await _db.BlogPosts
            .Include(b => b.BlogSubscriptions)
            .Include(b => b.AnnouncementEmailLogs)
            .FirstOrDefaultAsync(b => b.Id == blogId, cancellationToken);
        if (blog == null)
        {
            return new AnnouncementQueueResult { ErrorMessage = "Blog post not found." };
        }

        if (!blog.IsFeatureAnnouncement)
        {
            return new AnnouncementQueueResult { ErrorMessage = "Only feature-announcement blogs can send announcement emails." };
        }

        if (!blog.IsPublished)
        {
            return new AnnouncementQueueResult { ErrorMessage = "Publish the blog post before sending announcement emails." };
        }

        if (!resendFailedOnly && blog.AnnouncementQueuedAt.HasValue)
        {
            return new AnnouncementQueueResult { ErrorMessage = "This feature announcement has already been queued. Use resend failed if you need another attempt." };
        }

        if (resendFailedOnly)
        {
            var failedLogs = blog.AnnouncementEmailLogs.Where(l => l.Status == "Failed").ToList();
            if (failedLogs.Count == 0)
            {
                return new AnnouncementQueueResult { Success = true, RecipientCount = 0 };
            }

            foreach (var failed in failedLogs)
            {
                failed.Status = "Queued";
                failed.ErrorMessage = null;
                failed.SentAt = null;
                failed.LastAttemptedAt = null;
                failed.RetryCount = 0;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return new AnnouncementQueueResult { Success = true, RecipientCount = failedLogs.Count };
        }

        var recipients = await ResolveRecipientsAsync(blog.SendToAllSubscribers, blog.BlogSubscriptions.Select(s => s.SubscriptionId), cancellationToken);
        if (recipients.Count == 0)
        {
            return new AnnouncementQueueResult { ErrorMessage = "No subscribed recipients matched the selected subscriptions/plans." };
        }

        foreach (var recipient in recipients)
        {
            if (blog.AnnouncementEmailLogs.Any(log => log.SubscriberEmail.Equals(recipient.Email!, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _db.BlogAnnouncementEmailLogs.Add(new BlogAnnouncementEmailLog
            {
                BlogId = blog.Id,
                SubscriberEmail = recipient.Email!,
                SubscriberUserId = recipient.Id,
                Status = "Queued",
                CreatedAt = DateTime.UtcNow
            });
        }

        blog.AnnouncementQueuedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return new AnnouncementQueueResult { Success = true, RecipientCount = recipients.Count };
    }

    public async Task<int> ProcessQueuedEmailsAsync(int batchSize = 50, CancellationToken cancellationToken = default)
    {
        var batch = await _db.BlogAnnouncementEmailLogs
            .Where(log => log.Status == "Queued")
            .OrderBy(log => log.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            return 0;
        }

        var blogs = await _db.BlogPosts
            .Include(b => b.BlogImages)
            .Where(b => batch.Select(log => log.BlogId).Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var userIds = batch.Where(log => !string.IsNullOrWhiteSpace(log.SubscriberUserId)).Select(log => log.SubscriberUserId!).Distinct().ToList();
        var users = await _db.Users.Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, cancellationToken);

        var processed = 0;
        foreach (var log in batch)
        {
            processed++;
            log.LastAttemptedAt = DateTime.UtcNow;

            if (!blogs.TryGetValue(log.BlogId, out var blog))
            {
                log.Status = "Failed";
                log.ErrorMessage = "Blog post no longer exists.";
                continue;
            }

            if (!string.IsNullOrWhiteSpace(log.SubscriberUserId)
                && users.TryGetValue(log.SubscriberUserId, out var user)
                && !user.IsSubscribedToAnnouncements)
            {
                log.Status = "Failed";
                log.ErrorMessage = "Recipient unsubscribed from announcements.";
                continue;
            }

            try
            {
                var message = BuildEmailMessage(blog, log, users.TryGetValue(log.SubscriberUserId ?? string.Empty, out var recipient) ? recipient : null);
                var sent = await _emailSender.SendAsync(message, cancellationToken);
                if (sent)
                {
                    log.Status = "Sent";
                    log.ErrorMessage = null;
                    log.SentAt = DateTime.UtcNow;
                    continue;
                }

                log.RetryCount++;
                if (log.RetryCount >= 3)
                {
                    log.Status = "Failed";
                    log.ErrorMessage = "The email provider did not accept the message after multiple attempts.";
                }
            }
            catch (Exception ex)
            {
                log.RetryCount++;
                if (log.RetryCount >= 3)
                {
                    log.Status = "Failed";
                }
                log.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Failed to send feature-announcement email for blog {BlogId} to {Email}.", blog.Id, log.SubscriberEmail);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return processed;
    }

    private async Task<List<ApplicationUser>> ResolveRecipientsAsync(bool sendToAllSubscribers, IEnumerable<int>? subscriptionIds, CancellationToken cancellationToken)
    {
        var selectedPlanIds = (subscriptionIds ?? Enumerable.Empty<int>()).Distinct().ToList();
        var usersQuery = _db.Users
            .Include(u => u.Subscription)
            .Include(u => u.Organization)
            .Where(u => !string.IsNullOrWhiteSpace(u.Email) && u.IsSubscribedToAnnouncements);

        if (!sendToAllSubscribers && selectedPlanIds.Count > 0)
        {
            usersQuery = usersQuery.Where(u =>
                (u.Subscription != null && selectedPlanIds.Contains((int)u.Subscription.Plan))
                || (u.Organization != null && selectedPlanIds.Contains((int)u.Organization.Plan)));
        }
        else
        {
            usersQuery = usersQuery.Where(u =>
                (u.Subscription != null && u.Subscription.Plan != PlanType.Free)
                || (u.Organization != null && u.Organization.Plan != PlanType.Free));
        }

        return await usersQuery
            .GroupBy(u => u.Email!)
            .Select(g => g.OrderBy(u => u.Id).First())
            .ToListAsync(cancellationToken);
    }

    private AnnouncementEmailMessage BuildEmailMessage(BlogPost blog, BlogAnnouncementEmailLog log, ApplicationUser? recipient)
    {
        var baseUrl = (_configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
        var blogUrl = $"{baseUrl}/blog/{blog.Slug}";
        var unsubscribeUrl = string.IsNullOrWhiteSpace(log.SubscriberUserId)
            ? blogUrl
            : $"{baseUrl}/blog/announcements/unsubscribe?userId={Uri.EscapeDataString(log.SubscriberUserId)}&blogId={blog.Id}";
        var imageUrl = ToAbsoluteUrl(blog.FeaturedImagePath ?? blog.ImageUrl, baseUrl);
        var summary = string.IsNullOrWhiteSpace(blog.Summary) ? blog.Title : blog.Summary;
        var subject = string.IsNullOrWhiteSpace(blog.EmailSubject) ? $"New feature: {blog.Title}" : blog.EmailSubject!.Trim();
        var safeTitle = System.Net.WebUtility.HtmlEncode(blog.Title);
        var safeSummary = System.Net.WebUtility.HtmlEncode(summary);
        var safeName = System.Net.WebUtility.HtmlEncode(recipient?.FullName ?? log.SubscriberEmail);

        var html = $@"<div style='font-family:Inter,Arial,sans-serif;background:#f5f7fb;padding:24px;'>
<div style='max-width:640px;margin:0 auto;background:#ffffff;border-radius:18px;border:1px solid #d9e3ef;overflow:hidden;'>
  <div style='padding:28px 28px 0;'>
    <div style='font-size:12px;font-weight:700;color:#4a8ec9;text-transform:uppercase;letter-spacing:.08em;'>Feature announcement</div>
    <h1 style='margin:10px 0 14px;color:#1e3a5f;font-size:28px;line-height:1.2;'>{safeTitle}</h1>
    <p style='margin:0 0 18px;color:#596d82;font-size:15px;'>Hello {safeName},</p>
    <p style='margin:0 0 22px;color:#374151;font-size:15px;line-height:1.7;'>{safeSummary}</p>
  </div>
  {(string.IsNullOrWhiteSpace(imageUrl) ? string.Empty : $"<img src='{imageUrl}' alt='{safeTitle}' style='display:block;width:100%;max-height:320px;object-fit:cover;' />")}
  <div style='padding:28px;'>
    <a href='{blogUrl}' style='display:inline-block;background:#4a8ec9;color:#ffffff;text-decoration:none;padding:14px 22px;border-radius:10px;font-weight:600;'>Read the announcement</a>
    <p style='margin:18px 0 0;color:#596d82;font-size:13px;line-height:1.6;'>If you no longer want feature emails, <a href='{unsubscribeUrl}' style='color:#4a8ec9;'>unsubscribe here</a>.</p>
  </div>
</div>
</div>";

        return new AnnouncementEmailMessage
        {
            ToEmail = log.SubscriberEmail,
            ToName = recipient?.FullName ?? log.SubscriberEmail,
            Subject = subject,
            HtmlBody = html
        };
    }

    private static string? ToAbsoluteUrl(string? path, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl}/{path.TrimStart('/')}";
    }
}

public class BlogAnnouncementQueueWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlogAnnouncementQueueWorker> _logger;

    public BlogAnnouncementQueueWorker(IServiceScopeFactory scopeFactory, ILogger<BlogAnnouncementQueueWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IBlogAnnouncementService>();
                await service.ProcessQueuedEmailsAsync(50, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Blog announcement queue processing failed.");
            }
        }
    }
}
