using Oes.Infrastructure.Work;

namespace Oes.Worker;

/// <summary>Every 15 minutes: due/overdue tasks, stale opportunities, expiring estimates and sub insurance, booking reminders.</summary>
public sealed class ChecksJob(IServiceScopeFactory scopes, ILogger<ChecksJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                var r = await ScheduledChecks.RunAllAsync(scopes, stoppingToken);
                if (r.Alerts + r.Reminders > 0) log.LogInformation("Scheduled checks: {Alerts} alerts, {Reminders} booking reminders", r.Alerts, r.Reminders);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Scheduled checks failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Once a day (first run shortly after start): delete old notifications and dead public links.</summary>
public sealed class CleanupJob(IServiceScopeFactory scopes, ILogger<CleanupJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await using var s = scopes.CreateAsyncScope();
                var r = await s.ServiceProvider.GetRequiredService<NightlyCleanup>().RunAsync(stoppingToken);
                log.LogInformation("Cleanup removed {Notifications} old notifications and {Links} dead links", r.Notifications, r.Links);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Cleanup failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
