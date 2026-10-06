using Microsoft.EntityFrameworkCore;
using Signage.Domain;
using Signage.Infrastructure;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Services;

public sealed class PresentationLibraryService(
    IDbContextFactory<SignageDbContext> dbFactory, ContentDeletionProcessor contentDeletions, TimeProvider timeProvider)
{
    public async Task ArchiveAsync(Guid id, bool archive, string actor, CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var presentation = await db.Presentations.SingleOrDefaultAsync(item => item.Id == id, token)
            ?? throw new InvalidOperationException("The presentation was not found.");
        presentation.ArchivedUtc = archive ? timeProvider.GetUtcNow() : null;
        if (archive)
        {
            await db.Publications.Where(item => item.PresentationVersion.PresentationId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.IsEnabled, false), token);
            await db.PublishTargets.Where(item => item.PresentationVersion.PresentationId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.PublishWhenReady, false), token);
        }
        db.AuditEvents.Add(Audit(actor, archive ? "PresentationArchived" : "PresentationRestored", nameof(Presentation), id,
            $"{(archive ? "Archived" : "Restored")} {presentation.Name}."));
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var presentation = await db.Presentations.Include(item => item.Versions).SingleOrDefaultAsync(item => item.Id == id, token)
            ?? throw new InvalidOperationException("The presentation was not found.");
        if (await db.ConversionJobs.AnyAsync(item => item.PresentationVersion.PresentationId == id && item.Status == ConversionJobStatus.Processing, token))
            throw new InvalidOperationException("This PowerPoint is being prepared. Wait for conversion to finish before deleting it.");
        var versionIds = presentation.Versions.Select(item => item.Id).ToArray();
        await db.Publications.Where(item => versionIds.Contains(item.PresentationVersionId)).ExecuteDeleteAsync(token);
        await db.Devices.Where(item => item.PlayingPresentationVersionId != null && versionIds.Contains(item.PlayingPresentationVersionId.Value))
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.PlayingPresentationVersionId, (Guid?)null)
                .SetProperty(item => item.PlayingContentId, (string?)null), token);
        foreach (var key in presentation.Versions.Select(item => item.SourceStorageKey).Distinct())
            db.PendingContentDeletions.Add(new PendingContentDeletion { Id = Guid.NewGuid(), StorageKey = key, CreatedUtc = timeProvider.GetUtcNow() });
        foreach (var contentId in presentation.Versions.Where(item => item.ContentId != null).Select(item => item.ContentId!).Distinct())
            db.PendingContentDeletions.Add(new PendingContentDeletion { Id = Guid.NewGuid(), StorageKey = contentId, IsPackage = true, CreatedUtc = timeProvider.GetUtcNow() });
        db.Presentations.Remove(presentation);
        db.AuditEvents.Add(Audit(actor, "PresentationDeleted", nameof(Presentation), id, $"Deleted {presentation.Name} and its {presentation.Versions.Count} version(s)."));
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        // If a file is locked, the durable queue retries without undoing the library deletion.
        await contentDeletions.PurgeAsync(token);
    }

    public async Task<Guid> SaveFolderAsync(Guid? id, string? name, string actor, CancellationToken token)
    {
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            throw new InvalidOperationException("Enter a folder name between 1 and 120 characters.");
        var normalized = name.ToUpperInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(token);
        if (await db.PresentationFolders.AnyAsync(item => item.NormalizedName == normalized && item.Id != id, token))
            throw new InvalidOperationException("A folder with that name already exists.");
        var folder = id is Guid folderId ? await db.PresentationFolders.SingleOrDefaultAsync(item => item.Id == folderId, token)
            ?? throw new InvalidOperationException("The folder was not found.")
            : new PresentationFolder { Id = Guid.NewGuid(), Name = name, NormalizedName = normalized, CreatedUtc = timeProvider.GetUtcNow() };
        if (id is null) db.PresentationFolders.Add(folder);
        folder.Name = name;
        folder.NormalizedName = normalized;
        db.AuditEvents.Add(Audit(actor, id is null ? "PresentationFolderCreated" : "PresentationFolderRenamed", nameof(PresentationFolder), folder.Id, $"Saved folder {name}."));
        await db.SaveChangesAsync(token);
        return folder.Id;
    }

    public async Task DeleteFolderAsync(Guid id, string actor, CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        var folder = await db.PresentationFolders.SingleOrDefaultAsync(item => item.Id == id, token)
            ?? throw new InvalidOperationException("The folder was not found.");
        db.PresentationFolders.Remove(folder); // ON DELETE SET NULL keeps its presentations.
        db.AuditEvents.Add(Audit(actor, "PresentationFolderDeleted", nameof(PresentationFolder), id, $"Removed folder {folder.Name}; its presentations are now unfiled."));
        await db.SaveChangesAsync(token);
    }

    public async Task MoveAsync(Guid id, Guid? folderId, string actor, CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        var presentation = await db.Presentations.SingleOrDefaultAsync(item => item.Id == id, token)
            ?? throw new InvalidOperationException("The presentation was not found.");
        if (folderId is not null && !await db.PresentationFolders.AnyAsync(item => item.Id == folderId, token))
            throw new InvalidOperationException("The folder was not found.");
        presentation.FolderId = folderId;
        db.AuditEvents.Add(Audit(actor, "PresentationMoved", nameof(Presentation), id, $"Moved {presentation.Name} to {(folderId is null ? "Unfiled" : "a folder")}."));
        await db.SaveChangesAsync(token);
    }

    private AuditEvent Audit(string actor, string action, string entity, Guid id, string summary) => new()
    {
        Id = Guid.NewGuid(), OccurredUtc = timeProvider.GetUtcNow(), ActorType = "Human", ActorId = actor,
        Action = action, EntityType = entity, EntityId = id.ToString(), Summary = summary
    };
}
