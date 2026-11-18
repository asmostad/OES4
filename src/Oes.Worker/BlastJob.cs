using Oes.Infrastructure.Communication;

namespace Oes.Worker;

/// <summary>Every minute: send one throttled batch of each due email blast.</summary>
public sealed class BlastJob(IServiceScopeFactory scopes, ILogger<BlastJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                var sent = await BlastSender.RunDueAsync(scopes, stoppingToken);
                if (sent > 0) log.LogInformation("Blast sender sent {Count} emails", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Blast sender run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
