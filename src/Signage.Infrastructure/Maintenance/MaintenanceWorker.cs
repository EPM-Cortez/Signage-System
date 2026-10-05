using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signage.Application;

namespace Signage.Infrastructure;

public sealed class MaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageOptions> storageOptions,
    ILogger<MaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(storageOptions.Value.CleanupIntervalMinutes));
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var result = await scope.ServiceProvider.GetRequiredService<MaintenanceProcessor>()
                        .RunAsync(null, stoppingToken);
                    logger.LogInformation(
                        "Cleanup completed dryRun={DryRun} versions={Versions} pairings={Pairings} temporary={Temporary}",
                        result.DryRun,
                        result.ArchivedVersions,
                        result.ExpiredPairingSessions,
                        result.TemporaryItems);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "The scheduled cleanup pass failed; the next pass will retry.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
