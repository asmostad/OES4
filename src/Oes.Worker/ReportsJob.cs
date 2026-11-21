using Oes.Infrastructure.Reports;

namespace Oes.Worker;

/// <summary>Every 15 minutes: email saved reports whose weekly or monthly send time has come.</summary>
public sealed class ReportsJob(IServiceScopeFactory scopes, ILogger<ReportsJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                var sent = await SavedReportRunner.RunAllAsync(scopes, stoppingToken);
                if (sent > 0) log.LogInformation("Scheduled reports: {Sent} sent", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Scheduled reports failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
