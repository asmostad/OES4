namespace Oes.Worker;

/// <summary>Logs a heartbeat so the service's health is visible until real jobs exist.</summary>
public sealed class Heartbeat(ILogger<Heartbeat> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("OES4 Worker started");
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do { log.LogInformation("OES4 Worker heartbeat at {Time:O}", DateTimeOffset.UtcNow); }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
