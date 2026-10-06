namespace Signage.Domain;

public enum PresentationVersionStatus
{
    Uploaded,
    Queued,
    Converting,
    Ready,
    Failed,
    Archived
}

public enum ConversionJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public sealed class Presentation
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string CreatedBySubject { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ArchivedUtc { get; set; }
    public Guid? FolderId { get; set; }
    public PresentationFolder? Folder { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public List<PresentationVersion> Versions { get; set; } = [];
}

public sealed class PresentationFolder
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}

// Durable file cleanup after a presentation is deleted from the library.
public sealed class PendingContentDeletion
{
    public Guid Id { get; set; }
    public required string StorageKey { get; set; }
    public bool IsPackage { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class PresentationVersion
{
    public Guid Id { get; set; }
    public Guid PresentationId { get; set; }
    public Presentation Presentation { get; set; } = null!;
    public int VersionNumber { get; set; }
    public required string OriginalFileName { get; set; }
    public required string SourceStorageKey { get; set; }
    public required string SourceSha256 { get; set; }
    public PresentationVersionStatus Status { get; set; }
    public int SlideCount { get; set; }
    public long TotalDurationMs { get; set; }
    public string? ContentId { get; set; }
    public string? ManifestStorageKey { get; set; }
    public string? DiagnosticsJson { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureDetail { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ReadyUtc { get; set; }
    public long ConcurrencyToken { get; set; }
    public List<ConversionJob> Jobs { get; set; } = [];
    public List<PublishTarget> PublishTargets { get; set; } = [];
}

public sealed class ConversionJob
{
    public Guid Id { get; set; }
    public Guid PresentationVersionId { get; set; }
    public PresentationVersion PresentationVersion { get; set; } = null!;
    public ConversionJobStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset AvailableUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
}

public sealed class ScreenGroup
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public List<Device> Devices { get; set; } = [];
    public List<Publication> Publications { get; set; } = [];
}

public sealed class PublishTarget
{
    public Guid PresentationVersionId { get; set; }
    public PresentationVersion PresentationVersion { get; set; } = null!;
    public Guid ScreenGroupId { get; set; }
    public ScreenGroup ScreenGroup { get; set; } = null!;
    public DateTimeOffset StartsUtc { get; set; }
    public DateTimeOffset? EndsUtc { get; set; }
    public int Priority { get; set; }
    public bool PublishWhenReady { get; set; }
}

public sealed class Publication
{
    public Guid Id { get; set; }
    public Guid ScreenGroupId { get; set; }
    public ScreenGroup ScreenGroup { get; set; } = null!;
    public Guid PresentationVersionId { get; set; }
    public PresentationVersion PresentationVersion { get; set; } = null!;
    public DateTimeOffset StartsUtc { get; set; }
    public DateTimeOffset? EndsUtc { get; set; }
    public int Priority { get; set; }
    public bool IsEnabled { get; set; }
    public required string PublishedBySubject { get; set; }
    public DateTimeOffset PublishedUtc { get; set; }
}

public sealed class Device
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Guid? ScreenGroupId { get; set; }
    public ScreenGroup? ScreenGroup { get; set; }
    public string? TokenHash { get; set; }
    public DateTimeOffset? PairedUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public DateTimeOffset? ArchivedUtc { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public string? LastIpAddress { get; set; }
    public string? BrowserSummary { get; set; }
    public string? PlayingContentId { get; set; }
    public Guid? PlayingPresentationVersionId { get; set; }
    public string? LastError { get; set; }
}

public sealed class PairingSession
{
    public Guid Id { get; set; }
    public required string CodeHash { get; set; }
    public required string TemporaryTokenHash { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public Guid? ApprovedDeviceId { get; set; }
    public Device? ApprovedDevice { get; set; }
    public string? ProtectedDeviceToken { get; set; }
    public DateTimeOffset? ConsumedUtc { get; set; }
}

public sealed class PublisherAccess
{
    public required string IdentitySubject { get; set; }
    public Guid ScreenGroupId { get; set; }
    public ScreenGroup ScreenGroup { get; set; } = null!;
}

public enum StaffRole
{
    Pending,
    Teacher,
    Administrator
}

// Permissions are local to signage; no directory password is ever persisted.
public sealed class StaffAccount
{
    public Guid Id { get; set; }
    public required string IdentitySubject { get; set; }
    public required string Provider { get; set; }
    public string? ExternalSubject { get; set; }
    public required string LoginName { get; set; }
    public required string NormalizedLoginName { get; set; }
    public required string DisplayName { get; set; }
    public StaffRole Role { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? LastSignInUtc { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public required string ActorType { get; set; }
    public required string ActorId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Summary { get; set; }
    public string? DetailJson { get; set; }
}
