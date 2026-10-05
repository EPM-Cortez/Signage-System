using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Signage.Domain;

namespace Signage.Infrastructure.Persistence;

public sealed class SignageDbContext(DbContextOptions<SignageDbContext> options) : DbContext(options)
{
    public DbSet<Presentation> Presentations => Set<Presentation>();
    public DbSet<PresentationVersion> PresentationVersions => Set<PresentationVersion>();
    public DbSet<ConversionJob> ConversionJobs => Set<ConversionJob>();
    public DbSet<ScreenGroup> ScreenGroups => Set<ScreenGroup>();
    public DbSet<PublishTarget> PublishTargets => Set<PublishTarget>();
    public DbSet<Publication> Publications => Set<Publication>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PairingSession> PairingSessions => Set<PairingSession>();
    public DbSet<PublisherAccess> PublisherAccess => Set<PublisherAccess>();
    public DbSet<StaffAccount> StaffAccounts => Set<StaffAccount>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot natively order or range-query DateTimeOffset values. Every
        // persisted timestamp is normalized to UTC ticks so leases and schedules
        // retain their chronological ordering in SQL.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToUtcTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Presentation>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(200);
            entity.Property(item => item.CreatedBySubject).HasMaxLength(300);
            entity.HasMany(item => item.Versions).WithOne(item => item.Presentation).HasForeignKey(item => item.PresentationId);
        });

        modelBuilder.Entity<PresentationVersion>(entity =>
        {
            entity.HasIndex(item => new { item.PresentationId, item.VersionNumber }).IsUnique();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.OriginalFileName).HasMaxLength(260);
            entity.Property(item => item.SourceStorageKey).HasMaxLength(500);
            entity.Property(item => item.SourceSha256).HasMaxLength(64);
            entity.Property(item => item.ContentId).HasMaxLength(64);
            entity.Property(item => item.ManifestStorageKey).HasMaxLength(500);
            entity.Property(item => item.FailureCode).HasMaxLength(64);
            entity.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
        });

        modelBuilder.Entity<ConversionJob>(entity =>
        {
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(item => new { item.Status, item.AvailableUtc });
            entity.HasOne(item => item.PresentationVersion).WithMany(item => item.Jobs)
                .HasForeignKey(item => item.PresentationVersionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScreenGroup>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(120);
            entity.HasIndex(item => item.Name).IsUnique();
        });

        modelBuilder.Entity<PublishTarget>(entity =>
        {
            entity.HasKey(item => new { item.PresentationVersionId, item.ScreenGroupId });
            entity.HasOne(item => item.PresentationVersion).WithMany(item => item.PublishTargets)
                .HasForeignKey(item => item.PresentationVersionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.ScreenGroup).WithMany()
                .HasForeignKey(item => item.ScreenGroupId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Publication>(entity =>
        {
            entity.HasIndex(item => new { item.ScreenGroupId, item.IsEnabled, item.StartsUtc });
            entity.HasOne(item => item.ScreenGroup).WithMany(item => item.Publications)
                .HasForeignKey(item => item.ScreenGroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.PresentationVersion).WithMany()
                .HasForeignKey(item => item.PresentationVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(160);
            entity.Property(item => item.TokenHash).HasMaxLength(64);
            entity.HasIndex(item => item.TokenHash).IsUnique();
            entity.HasOne(item => item.ScreenGroup).WithMany(item => item.Devices)
                .HasForeignKey(item => item.ScreenGroupId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PairingSession>(entity =>
        {
            entity.Property(item => item.CodeHash).HasMaxLength(64);
            entity.Property(item => item.TemporaryTokenHash).HasMaxLength(64);
            entity.HasIndex(item => item.CodeHash);
            entity.HasIndex(item => item.TemporaryTokenHash).IsUnique();
            entity.HasOne(item => item.ApprovedDevice).WithMany()
                .HasForeignKey(item => item.ApprovedDeviceId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PublisherAccess>(entity =>
        {
            entity.HasKey(item => new { item.IdentitySubject, item.ScreenGroupId });
            entity.Property(item => item.IdentitySubject).HasMaxLength(300);
            entity.HasOne(item => item.ScreenGroup).WithMany()
                .HasForeignKey(item => item.ScreenGroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StaffAccount>(entity =>
        {
            entity.Property(item => item.IdentitySubject).HasMaxLength(300);
            entity.HasIndex(item => item.IdentitySubject).IsUnique();
            entity.Property(item => item.Provider).HasMaxLength(32);
            entity.Property(item => item.ExternalSubject).HasMaxLength(500);
            entity.HasIndex(item => new { item.Provider, item.ExternalSubject }).IsUnique();
            entity.Property(item => item.LoginName).HasMaxLength(300);
            entity.Property(item => item.NormalizedLoginName).HasMaxLength(300);
            entity.HasIndex(item => new { item.Provider, item.NormalizedLoginName }).IsUnique();
            entity.Property(item => item.DisplayName).HasMaxLength(200);
            entity.Property(item => item.Role).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasIndex(item => item.OccurredUtc);
            entity.Property(item => item.ActorType).HasMaxLength(32);
            entity.Property(item => item.ActorId).HasMaxLength(300);
            entity.Property(item => item.Action).HasMaxLength(100);
            entity.Property(item => item.EntityType).HasMaxLength(100);
            entity.Property(item => item.EntityId).HasMaxLength(100);
            entity.Property(item => item.Summary).HasMaxLength(500);
        });
    }

    private sealed class DateTimeOffsetToUtcTicksConverter()
        : ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
}
