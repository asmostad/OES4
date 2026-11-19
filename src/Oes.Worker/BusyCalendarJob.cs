using Oes.Infrastructure.Scheduling;

namespace Oes.Worker;

/// <summary>Every 15 minutes: re-read outside calendars (.ics feeds) so their busy times block online booking.</summary>
public sealed class BusyCalendarJob(IServiceScopeFactory scopes, ILogger<BusyCalendarJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                var n = await BusyCalendarRunner.RefreshAllAsync(scopes, stoppingToken);
                if (n > 0) log.LogInformation("Busy calendars: {Count} refreshed", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Busy calendar refresh failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
