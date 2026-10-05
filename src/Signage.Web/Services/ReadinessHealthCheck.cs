using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Services;

public sealed class ReadinessHealthCheck(
    DatabaseInitializer database,
    IContentStorage storage,
    IConverterRunner converter,
    IOptions<StorageOptions> storageOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!await database.CanConnectAsync(cancellationToken))
        {
            return HealthCheckResult.Unhealthy("SQLite is unavailable.");
        }
        if (!await storage.CanWriteAsync(cancellationToken))
        {
            return HealthCheckResult.Unhealthy("Content storage is not writable.");
        }
        if (storage.GetAvailableBytes() < storageOptions.Value.MinimumFreeBytes)
        {
            return HealthCheckResult.Degraded("Content storage is low on free space.");
        }
        if (!await converter.CheckHealthAsync(cancellationToken))
        {
            return HealthCheckResult.Degraded("The converter is unavailable; existing signage can continue playing.");
        }
        return HealthCheckResult.Healthy();
    }
}
