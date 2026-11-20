using Oes.Infrastructure.Work;

namespace Oes.Worker;

/// <summary>Every 30 seconds: push new notifications to users' phones (see PushService).</summary>
public sealed class PushJob(IServiceScopeFactory scopes, ILogger<PushJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                var r = await PushService.RunAllAsync(scopes, stoppingToken);
                if (r.Sent + r.Failed > 0) log.LogInformation("Pushed {Sent} ({Failed} failed, {Disabled} phones switched off) for {Notifications} notifications", r.Sent, r.Failed, r.Disabled, r.Notifications);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Push run failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
