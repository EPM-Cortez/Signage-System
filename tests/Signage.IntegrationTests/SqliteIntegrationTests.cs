using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;
using Signage.Infrastructure.Storage;

namespace Signage.IntegrationTests;

public sealed class SqliteIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"signage-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Migrations_create_real_sqlite_schema_with_wal_and_foreign_keys()
    {
        var factory = CreateFactory();
        var initializer = new DatabaseInitializer(factory, Options.Create(new DatabaseOptions { EnableWal = true, BusyTimeoutSeconds = 5 }));
        await initializer.MigrateAsync(CancellationToken.None);
        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await db.Database.CanConnectAsync());
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
        var foreignKeys = await ScalarAsync(db, "PRAGMA foreign_keys;");
        var journalMode = await TextScalarAsync(db, "PRAGMA journal_mode;");
        Assert.Equal(1L, foreignKeys);
        Assert.Equal("wal", journalMode, ignoreCase: true);
    }

    [Fact]
    public void A_second_instance_cannot_acquire_the_same_data_root_lock()
    {
        Directory.CreateDirectory(root);
        var options = Options.Create(new DatabaseOptions { InstanceLockPath = Path.Combine(root, "signage.lock") });
        using var first = new DataRootInstanceLock(options);
        Assert.Throws<InvalidOperationException>(() => new DataRootInstanceLock(options));
        first.Dispose();
        using var recovered = new DataRootInstanceLock(options);
    }

    [Fact]
    public async Task Publication_and_job_records_are_transactionally_persisted()
    {
        var factory = CreateFactory();
        await new DatabaseInitializer(factory, Options.Create(new DatabaseOptions())).MigrateAsync(CancellationToken.None);
        var groupId = Guid.NewGuid();
        var presentationId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.ScreenGroups.Add(new ScreenGroup { Id = groupId, Name = "Reception", CreatedUtc = DateTimeOffset.UtcNow });
            db.Presentations.Add(new Presentation { Id = presentationId, Name = "Welcome", CreatedBySubject = "teacher", CreatedUtc = DateTimeOffset.UtcNow });
            db.PresentationVersions.Add(new PresentationVersion { Id = versionId, PresentationId = presentationId, VersionNumber = 1, OriginalFileName = "welcome.pptx", SourceStorageKey = "sources/test", SourceSha256 = new string('a', 64), Status = PresentationVersionStatus.Queued, CreatedUtc = DateTimeOffset.UtcNow });
            db.ConversionJobs.Add(new ConversionJob { Id = Guid.NewGuid(), PresentationVersionId = versionId, Status = ConversionJobStatus.Queued, AvailableUtc = DateTimeOffset.UtcNow, CreatedUtc = DateTimeOffset.UtcNow });
            db.PublishTargets.Add(new PublishTarget { PresentationVersionId = versionId, ScreenGroupId = groupId, StartsUtc = DateTimeOffset.UtcNow, PublishWhenReady = true });
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal(1, await verify.ConversionJobs.CountAsync());
        Assert.Equal(1, await verify.PublishTargets.CountAsync());
    }

    [Fact]
    public async Task Publication_permission_query_defaults_to_deny_and_tracks_revoked_grants()
    {
        var factory = CreateFactory();
        await new DatabaseInitializer(factory, Options.Create(new DatabaseOptions())).MigrateAsync(CancellationToken.None);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        db.ScreenGroups.AddRange(new ScreenGroup { Id = first, Name = "Reception", CreatedUtc = DateTimeOffset.UtcNow },
            new ScreenGroup { Id = second, Name = "Staff Room", CreatedUtc = DateTimeOffset.UtcNow });
        var account = new StaffAccount { Id = Guid.NewGuid(), IdentitySubject = "teacher", ExternalSubject = "guid", Provider = "ActiveDirectory", LoginName = "teacher@school.test",
            NormalizedLoginName = "teacher@school.test", DisplayName = "Teacher", Role = StaffRole.Teacher, CreatedUtc = DateTimeOffset.UtcNow };
        db.StaffAccounts.Add(account);
        var grant = new PublisherAccess { IdentitySubject = "teacher", ScreenGroupId = first }; db.PublisherAccess.Add(grant);
        await db.SaveChangesAsync();
        Assert.Equal(first, Assert.Single(await StaffPermissions.AllowedGroupsAsync(db, "teacher", CancellationToken.None)));
        Assert.Empty(await StaffPermissions.AllowedGroupsAsync(db, "unknown", CancellationToken.None));
        db.PublisherAccess.Remove(grant); await db.SaveChangesAsync();
        Assert.Empty(await StaffPermissions.AllowedGroupsAsync(db, "teacher", CancellationToken.None));
        account.Role = StaffRole.Administrator; await db.SaveChangesAsync();
        Assert.Equal(2, (await StaffPermissions.AllowedGroupsAsync(db, "teacher", CancellationToken.None)).Count);
        account.IsEnabled = false; await db.SaveChangesAsync();
        Assert.Empty(await StaffPermissions.AllowedGroupsAsync(db, "teacher", CancellationToken.None));
    }

    private TestDbContextFactory CreateFactory()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "signage.db");
        var options = new DbContextOptionsBuilder<SignageDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True;Default Timeout=5")
            .Options;
        return new TestDbContextFactory(options);
    }

    private static async Task<long> ScalarAsync(SignageDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection(); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
    private static async Task<string> TextScalarAsync(SignageDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection(); if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        TestDataCleanup.DeleteDirectory(root);
    }

    private sealed class TestDbContextFactory(DbContextOptions<SignageDbContext> options) : IDbContextFactory<SignageDbContext>
    {
        public SignageDbContext CreateDbContext() => new(options);
        public Task<SignageDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SignageDbContext(options));
    }
}
