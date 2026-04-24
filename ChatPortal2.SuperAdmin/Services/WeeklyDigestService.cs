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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now.DayOfWeek == DayOfWeek.Monday && now.Hour == 9)
                {
                    await _sender.TrySendDigestAsync(null, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WeeklyDigestService failed.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}

