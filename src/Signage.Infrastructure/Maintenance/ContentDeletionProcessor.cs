using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Signage.Application;
using Signage.Infrastructure.Persistence;

namespace Signage.Infrastructure;

public sealed class ContentDeletionProcessor(
    IDbContextFactory<SignageDbContext> dbFactory,
    IContentStorage storage,
    ILogger<ContentDeletionProcessor> logger)
{
    private static readonly SemaphoreSlim PurgeLock = new(1, 1);

    public async Task PurgeAsync(CancellationToken token)
    {
        await PurgeLock.WaitAsync(token);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(token);
            var pending = await db.PendingContentDeletions.OrderBy(item => item.CreatedUtc).ToListAsync(token);
            foreach (var item in pending)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(token);
                    var retained = item.IsPackage
                        ? await db.PresentationVersions.AnyAsync(version => version.ContentId == item.StorageKey, token)
                        : await db.PresentationVersions.AnyAsync(version => version.SourceStorageKey == item.StorageKey, token);
                    if (!retained)
                    {
                        // Storage resolves these internally generated keys inside its configured root.
                        var path = item.IsPackage ? storage.GetPackagePath(item.StorageKey) : storage.GetSourcePath(item.StorageKey);
                        if (item.IsPackage && Directory.Exists(path)) Directory.Delete(path, recursive: true);
                        else if (!item.IsPackage && File.Exists(path)) File.Delete(path);
                    }
                    db.PendingContentDeletions.Remove(item);
                    await db.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "File deletion {DeletionId} will be retried during scheduled cleanup.", item.Id);
                }
            }
        }
        finally { PurgeLock.Release(); }
    }
}
