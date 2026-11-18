using Oes.Infrastructure.Communication;

namespace Oes.Worker;

/// <summary>Every minute: run the nurture steps that are due.</summary>
public sealed class NurtureJob(IServiceScopeFactory scopes, ILogger<NurtureJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                var ran = await NurtureRunner.RunDueAsync(scopes, ct: stoppingToken);
                if (ran > 0) log.LogInformation("Nurture runner ran {Count} steps", ran);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Nurture runner failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
