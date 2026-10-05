using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Services;

public sealed class PresentationService(
    IDbContextFactory<SignageDbContext> dbFactory,
    IContentStorage storage,
    IOptions<UploadOptions> uploadOptions,
    IOptions<StorageOptions> storageOptions,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<ScreenGroup>> GetAvailableGroupsAsync(
        string subject,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // Never treat a caller-supplied administrator flag as permission authority.
        var allowed = await StaffPermissions.AllowedGroupsAsync(db, subject, cancellationToken);
        return await db.ScreenGroups.AsNoTracking().Where(item => allowed.Contains(item.Id)).OrderBy(item => item.Name).ToListAsync(cancellationToken);
    }

    public async Task<Guid> UploadAsync(
        string subject,
        bool isAdministrator,
        string? requestedName,
        string originalFileName,
        long length,
        Stream source,
        IReadOnlyCollection<Guid> requestedGroupIds,
        bool publishWhenReady,
        DateTimeOffset? startsUtc,
        DateTimeOffset? endsUtc,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(originalFileName), ".pptx", StringComparison.OrdinalIgnoreCase))
        {
            throw new PresentationRejectedException("INVALID_PPTX", "Only .pptx PowerPoint files are accepted.");
        }
        if (length <= 0 || length > uploadOptions.Value.MaximumBytes)
        {
            throw new PresentationRejectedException("UPLOAD_SIZE_LIMIT", "The upload is empty or exceeds the configured size limit.");
        }
        if (storage.GetAvailableBytes() < length + storageOptions.Value.MinimumFreeBytes)
        {
            throw new PresentationRejectedException("STORAGE_SPACE_LOW", "There is not enough free disk space for this upload.");
        }

        var groupIds = requestedGroupIds.Distinct().ToArray();
        if (groupIds.Length == 0)
        {
            throw new ArgumentException("Select at least one screen group.", nameof(requestedGroupIds));
        }

        await using var permissionDb = await dbFactory.CreateDbContextAsync(cancellationToken);
        var allowed = await StaffPermissions.AllowedGroupsAsync(permissionDb, subject, cancellationToken);
        if (groupIds.Any(id => !allowed.Contains(id)))
        {
            throw new UnauthorizedAccessException("You cannot publish to one or more selected screen groups.");
        }

        var now = timeProvider.GetUtcNow();
        var versionId = Guid.NewGuid();
        var stored = await storage.SaveSourceAsync(source, versionId, cancellationToken);
        var presentationId = Guid.NewGuid();
        var cleanName = string.IsNullOrWhiteSpace(requestedName)
            ? Path.GetFileNameWithoutExtension(originalFileName)
            : requestedName.Trim();
        cleanName = cleanName.Length > 200 ? cleanName[..200] : cleanName;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        allowed = await StaffPermissions.AllowedGroupsAsync(db, subject, cancellationToken);
        if (groupIds.Any(id => !allowed.Contains(id))) throw new UnauthorizedAccessException("Publishing permissions changed during the upload.");
        var presentation = new Presentation
        {
            Id = presentationId,
            Name = cleanName,
            CreatedBySubject = subject,
            CreatedUtc = now
        };
        var version = new PresentationVersion
        {
            Id = versionId,
            PresentationId = presentationId,
            VersionNumber = 1,
            OriginalFileName = Path.GetFileName(originalFileName),
            SourceStorageKey = stored.Key,
            SourceSha256 = stored.Sha256,
            Status = PresentationVersionStatus.Queued,
            CreatedUtc = now
        };
        var job = new ConversionJob
        {
            Id = Guid.NewGuid(),
            PresentationVersionId = versionId,
            Status = ConversionJobStatus.Queued,
            AvailableUtc = now,
            CreatedUtc = now
        };
        db.Presentations.Add(presentation);
        db.PresentationVersions.Add(version);
        db.ConversionJobs.Add(job);
        foreach (var groupId in groupIds)
        {
            db.PublishTargets.Add(new PublishTarget
            {
                PresentationVersionId = versionId,
                ScreenGroupId = groupId,
                StartsUtc = startsUtc ?? now,
                EndsUtc = endsUtc,
                Priority = 0,
                PublishWhenReady = publishWhenReady
            });
        }
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredUtc = now,
            ActorType = "Human",
            ActorId = subject,
            Action = "PresentationUploaded",
            EntityType = nameof(PresentationVersion),
            EntityId = versionId.ToString(),
            Summary = $"Uploaded {Path.GetFileName(originalFileName)} for {groupIds.Length} screen group(s)."
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return versionId;
    }

    public async Task RetryAsync(Guid versionId, string actor, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var version = await db.PresentationVersions.SingleAsync(item => item.Id == versionId, cancellationToken);
        if (version.Status != PresentationVersionStatus.Failed)
        {
            throw new InvalidOperationException("Only failed versions can be retried.");
        }
        version.Status = PresentationVersionStatus.Queued;
        version.FailureCode = null;
        version.FailureDetail = null;
        version.ConcurrencyToken++;
        db.ConversionJobs.Add(new ConversionJob
        {
            Id = Guid.NewGuid(),
            PresentationVersionId = versionId,
            Status = ConversionJobStatus.Queued,
            AvailableUtc = timeProvider.GetUtcNow(),
            CreatedUtc = timeProvider.GetUtcNow()
        });
        db.AuditEvents.Add(Audit(actor, "ConversionRetried", nameof(PresentationVersion), versionId, "Conversion retry requested."));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PublishReadyAsync(Guid versionId, Guid screenGroupId, string actor, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var version = await db.PresentationVersions.SingleAsync(item => item.Id == versionId, cancellationToken);
        if (version.Status != PresentationVersionStatus.Ready)
        {
            throw new InvalidOperationException("Only ready content can be published.");
        }
        db.Publications.Add(new Publication
        {
            Id = Guid.NewGuid(),
            ScreenGroupId = screenGroupId,
            PresentationVersionId = versionId,
            StartsUtc = timeProvider.GetUtcNow(),
            Priority = 0,
            IsEnabled = true,
            PublishedBySubject = actor,
            PublishedUtc = timeProvider.GetUtcNow()
        });
        db.AuditEvents.Add(Audit(actor, "PublicationRolledBack", nameof(PresentationVersion), versionId, "A ready version was published as a rollback."));
        await db.SaveChangesAsync(cancellationToken);
    }

    private AuditEvent Audit(string actor, string action, string entityType, Guid entityId, string summary) => new()
    {
        Id = Guid.NewGuid(),
        OccurredUtc = timeProvider.GetUtcNow(),
        ActorType = "Human",
        ActorId = actor,
        Action = action,
        EntityType = entityType,
        EntityId = entityId.ToString(),
        Summary = summary
    };
}
