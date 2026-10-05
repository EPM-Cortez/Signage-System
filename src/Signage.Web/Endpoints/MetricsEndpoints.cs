using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;
using Signage.Web.Services;

namespace Signage.Web.Endpoints;

public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/metrics", async (
            IDbContextFactory<SignageDbContext> dbFactory,
            IOptions<SignageOptions> signageOptions,
            IOptions<StorageOptions> storageOptions,
            IWebHostEnvironment environment,
            OperationalMetrics runtimeMetrics,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var queued = await db.ConversionJobs.CountAsync(item => item.Status == ConversionJobStatus.Queued, cancellationToken);
            var successful = await db.ConversionJobs.CountAsync(item => item.Status == ConversionJobStatus.Completed, cancellationToken);
            var failed = await db.ConversionJobs.CountAsync(item => item.Status == ConversionJobStatus.Failed, cancellationToken);
            var published = await db.Publications.CountAsync(item => item.IsEnabled, cancellationToken);
            var devices = await db.Devices.AsNoTracking().Where(item => item.RevokedUtc == null).Select(item => item.LastSeenUtc).ToListAsync(cancellationToken);
            var offlineBefore = timeProvider.GetUtcNow().AddSeconds(-signageOptions.Value.OfflineAfterSeconds);
            var online = devices.Count(item => item >= offlineBefore);
            var durations = await db.PresentationVersions.AsNoTracking()
                .Where(item => item.DiagnosticsJson != null)
                .Select(item => item.DiagnosticsJson!)
                .ToListAsync(cancellationToken);
            var durationSeconds = durations.Select(TryReadDurationSeconds).Where(item => item is not null).Select(item => item!.Value).ToArray();
            var contentRoot = Path.GetFullPath(storageOptions.Value.RootPath, environment.ContentRootPath);
            var storageBytes = Directory.Exists(contentRoot)
                ? Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)
                : 0;

            var metrics = new StringBuilder()
                .AppendLine("# HELP signage_conversion_jobs_queued Current queued conversion jobs.")
                .AppendLine("# TYPE signage_conversion_jobs_queued gauge")
                .AppendLine($"signage_conversion_jobs_queued {queued}")
                .AppendLine("# TYPE signage_conversion_success_total counter")
                .AppendLine($"signage_conversion_success_total {successful}")
                .AppendLine("# TYPE signage_conversion_failure_total counter")
                .AppendLine($"signage_conversion_failure_total {failed}")
                .AppendLine("# TYPE signage_conversion_duration_seconds gauge")
                .AppendLine($"signage_conversion_duration_seconds {(durationSeconds.Length == 0 ? 0 : durationSeconds.Average()).ToString(CultureInfo.InvariantCulture)}")
                .AppendLine("# TYPE signage_publications_enabled gauge")
                .AppendLine($"signage_publications_enabled {published}")
                .AppendLine("# TYPE signage_devices_online gauge")
                .AppendLine($"signage_devices_online {online}")
                .AppendLine("# TYPE signage_devices_offline gauge")
                .AppendLine($"signage_devices_offline {devices.Count - online}")
                .AppendLine("# TYPE signage_assignment_errors_total counter")
                .AppendLine($"signage_assignment_errors_total {runtimeMetrics.AssignmentErrors}")
                .AppendLine("# TYPE signage_storage_bytes gauge")
                .AppendLine($"signage_storage_bytes {storageBytes}");
            return Results.Text(metrics.ToString(), "text/plain; version=0.0.4; charset=utf-8");
        }).RequireAuthorization("SignageAdmin");
        return app;
    }

    private static double? TryReadDurationSeconds(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("elapsedMs", out var elapsed) && elapsed.TryGetDouble(out var milliseconds)
                ? milliseconds / 1000d
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
