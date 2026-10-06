using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Infrastructure;

public sealed class MaintenanceProcessor(
    IDbContextFactory<SignageDbContext> dbFactory,
    IContentStorage storage,
    IHostEnvironment environment,
    IOptions<StorageOptions> storageOptions,
    IOptions<RenderingOptions> renderingOptions,
    TimeProvider timeProvider,
    ILogger<MaintenanceProcessor> logger,
    ContentDeletionProcessor contentDeletions)
{
    private readonly StorageOptions storageSettings = storageOptions.Value;
    private readonly RenderingOptions renderingSettings = renderingOptions.Value;

    public async Task<MaintenanceResult> RunAsync(bool? dryRunOverride, CancellationToken cancellationToken)
    {
        var dryRun = dryRunOverride ?? storageSettings.CleanupDryRun;
        var now = timeProvider.GetUtcNow();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var protectedVersionIds = await db.Publications.AsNoTracking()
            .Where(item => item.IsEnabled && (item.EndsUtc == null || item.EndsUtc > now))
            .Select(item => item.PresentationVersionId)
            .ToHashSetAsync(cancellationToken);
        var currentVersionIds = await db.Presentations.AsNoTracking()
            .Where(item => item.CurrentVersionId != null)
            .Select(item => item.CurrentVersionId!.Value)
            .ToHashSetAsync(cancellationToken);
        protectedVersionIds.UnionWith(currentVersionIds);
        // Archiving a whole presentation is reversible and must preserve its files.
        protectedVersionIds.UnionWith(await db.PresentationVersions
            .Where(item => item.Presentation.ArchivedUtc != null || item.Jobs.Any(job => job.Status == ConversionJobStatus.Queued || job.Status == ConversionJobStatus.Processing))
            .Select(item => item.Id).ToListAsync(cancellationToken));

        var versions = await db.PresentationVersions
            .Select(item => new VersionRecord(
                item.Id,
                item.PresentationId,
                item.CreatedUtc,
                item.SourceStorageKey,
                item.ContentId,
                protectedVersionIds.Contains(item.Id)))
            .ToListAsync(cancellationToken);
        var removableIds = RetentionRules.SelectRemovable(
            versions.Select(item => new VersionRetentionCandidate(item.Id, item.PresentationId, item.CreatedUtc, item.IsProtected)),
            storageSettings.RetainedVersionsPerPresentation);
        var retainedContentIds = versions
            .Where(item => !removableIds.Contains(item.Id) && item.ContentId is not null)
            .Select(item => item.ContentId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removedVersions = 0;
        foreach (var record in versions.Where(item => removableIds.Contains(item.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogInformation("Cleanup {Mode} archive presentation version {VersionId}", dryRun ? "would" : "will", record.Id);
            if (dryRun)
            {
                removedVersions++;
                continue;
            }

            DeleteFileIfPresent(storage.GetSourcePath(record.SourceStorageKey));
            if (record.ContentId is not null && !retainedContentIds.Contains(record.ContentId))
            {
                DeleteDirectoryIfPresent(storage.GetPackagePath(record.ContentId));
            }
            var entity = await db.PresentationVersions.SingleAsync(item => item.Id == record.Id, cancellationToken);
            entity.Status = PresentationVersionStatus.Archived;
            entity.ContentId = null;
            entity.ManifestStorageKey = null;
            entity.ConcurrencyToken++;
            removedVersions++;
        }

        var expiredPairings = await db.PairingSessions
            .Where(item => item.ExpiresUtc < now)
            .ToListAsync(cancellationToken);
        if (expiredPairings.Count > 0)
        {
            logger.LogInformation("Cleanup {Mode} remove {Count} expired pairing sessions", dryRun ? "would" : "will", expiredPairings.Count);
            if (!dryRun) db.PairingSessions.RemoveRange(expiredPairings);
        }

        if (!dryRun) await db.SaveChangesAsync(cancellationToken);
        if (!dryRun) await contentDeletions.PurgeAsync(cancellationToken);
        var temporaryItems = CleanTemporaryItems(now, dryRun);
        if (!dryRun) await db.Database.ExecuteSqlRawAsync("PRAGMA optimize;", cancellationToken);
        return new MaintenanceResult(dryRun, removedVersions, expiredPairings.Count, temporaryItems);
    }

    private int CleanTemporaryItems(DateTimeOffset now, bool dryRun)
    {
        var cutoff = now.UtcDateTime.AddHours(-storageSettings.TemporaryFileMaximumAgeHours);
        var count = 0;
        var tempRoot = Path.GetFullPath(renderingSettings.TempRoot, environment.ContentRootPath);
        if (Directory.Exists(tempRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(tempRoot, "attempt-*", SearchOption.TopDirectoryOnly))
            {
                if (Directory.GetLastWriteTimeUtc(directory) >= cutoff) continue;
                logger.LogInformation("Cleanup {Mode} remove temporary directory {Path}", dryRun ? "would" : "will", directory);
                if (!dryRun) DeleteDirectoryIfPresent(directory);
                count++;
            }
        }

        var contentRoot = Path.GetFullPath(storageSettings.RootPath, environment.ContentRootPath);
        var sourcesRoot = Path.Combine(contentRoot, "sources");
        if (Directory.Exists(sourcesRoot))
        {
            foreach (var file in Directory.EnumerateFiles(sourcesRoot, "*.uploading", SearchOption.TopDirectoryOnly))
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                logger.LogInformation("Cleanup {Mode} remove abandoned upload {Path}", dryRun ? "would" : "will", file);
                if (!dryRun) DeleteFileIfPresent(file);
                count++;
            }
        }
        return count;
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed record VersionRecord(
        Guid Id,
        Guid PresentationId,
        DateTimeOffset CreatedUtc,
        string SourceStorageKey,
        string? ContentId,
        bool IsProtected);
}

public sealed record MaintenanceResult(
    bool DryRun,
    int ArchivedVersions,
    int ExpiredPairingSessions,
    int TemporaryItems);
