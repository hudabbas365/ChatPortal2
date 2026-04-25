using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIInsights.SuperAdmin.Services;

public class WeeklyDigestService : BackgroundService
{
    private readonly DigestSenderService _sender;
    private readonly ILogger<WeeklyDigestService> _logger;

    public WeeklyDigestService(DigestSenderService sender, ILogger<WeeklyDigestService> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        DateTime? lastProcessedScheduledRunUtc = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var mostRecentScheduledRun = GetMostRecentScheduledRunUtc(now);

                // Fire if we haven't processed this scheduled run yet and the time has passed
                if ((!lastProcessedScheduledRunUtc.HasValue || lastProcessedScheduledRunUtc.Value < mostRecentScheduledRun)
                    && now >= mostRecentScheduledRun)
                {
                    await _sender.TrySendDigestAsync(null, stoppingToken);
                    lastProcessedScheduledRunUtc = mostRecentScheduledRun;
                }

                // Sleep until the next scheduled Monday 09:00 UTC
                now = DateTime.UtcNow;
                var nextRun = GetNextScheduledRunUtc(now);
                var delay = nextRun - now;
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WeeklyDigestService failed.");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    /// <summary>Returns the most recent Monday 09:00 UTC at or before <paramref name="utcNow"/>.</summary>
    private static DateTime GetMostRecentScheduledRunUtc(DateTime utcNow)
    {
        var daysSinceMonday = ((int)utcNow.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var scheduled = utcNow.Date.AddDays(-daysSinceMonday).AddHours(9);
        if (scheduled > utcNow)
            scheduled = scheduled.AddDays(-7);
        return scheduled;
    }

    /// <summary>Returns the next Monday 09:00 UTC strictly after <paramref name="utcNow"/>.</summary>
    private static DateTime GetNextScheduledRunUtc(DateTime utcNow)
    {
        var mostRecent = GetMostRecentScheduledRunUtc(utcNow);
        return mostRecent.AddDays(7);
    }
}

