using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure;
using Signage.Infrastructure.Conversion;
using Signage.Infrastructure.Persistence;
using Signage.Web.Services;

namespace Signage.IntegrationTests;

public sealed class LibraryManagementTests : IDisposable
{
    private readonly LibraryApplication app = new();

    [Fact]
    public async Task Devices_can_be_archived_restored_and_deleted_and_access_tracks_their_state()
    {
        using var admin = await AdminAsync();
        var deviceId = Guid.NewGuid();
        const string credential = "test-device-credential";
        await using (var db = await app.DbAsync())
        {
            db.Devices.Add(new Device { Id = deviceId, Name = "Retired screen", ScreenGroupId = DevelopmentSeeder.ReceptionId, TokenHash = TokenUtility.Hash(credential), LastSeenUtc = DateTimeOffset.UtcNow });
            db.PairingSessions.Add(new PairingSession { Id = Guid.NewGuid(), CodeHash = "code-hash", TemporaryTokenHash = "temporary-hash", ApprovedDeviceId = deviceId, ProtectedDeviceToken = "protected", ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5) });
            await db.SaveChangesAsync();
        }
        using var player = app.Client();
        player.DefaultRequestHeaders.Authorization = new("Bearer", credential);
        Assert.Equal(HttpStatusCode.OK, (await player.GetAsync("/api/player/assignment")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(admin, "/Admin/Devices", "Archive", ("deviceId", deviceId.ToString()))).StatusCode);
        Assert.DoesNotContain("Retired screen", await admin.GetStringAsync("/Admin/Devices"));
        Assert.Contains("Retired screen", await admin.GetStringAsync("/Admin/Devices?archived=true"));
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/api/player/assignment")).StatusCode);
        await PostAsync(admin, "/Admin/Devices", "Restore", ("deviceId", deviceId.ToString()));
        Assert.Equal(HttpStatusCode.OK, (await player.GetAsync("/api/player/assignment")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(admin, "/Admin/Devices", "Delete", ("deviceId", deviceId.ToString()))).StatusCode);
        await PostAsync(admin, "/Admin/Devices", "Delete", ("deviceId", deviceId.ToString()), ("confirmed", "true"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await player.GetAsync("/api/player/assignment")).StatusCode);
        await using var verify = await app.DbAsync();
        Assert.False(await verify.Devices.AnyAsync(item => item.Id == deviceId));
        Assert.False(await verify.PairingSessions.AnyAsync(item => item.ApprovedDeviceId == deviceId));
        Assert.Equal(3, await verify.AuditEvents.CountAsync(item => item.EntityId == deviceId.ToString()));
    }

    [Fact]
    public async Task Restoring_an_archived_revoked_device_does_not_restore_its_credentials()
    {
        using var admin = await AdminAsync();
        var id = Guid.NewGuid();
        await using (var db = await app.DbAsync())
        {
            db.Devices.Add(new Device { Id = id, Name = "Revoked screen", RevokedUtc = DateTimeOffset.UtcNow, ArchivedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await PostAsync(admin, "/Admin/Devices", "Restore", ("deviceId", id.ToString()));
        await using var verify = await app.DbAsync();
        var device = await verify.Devices.SingleAsync(item => item.Id == id);
        Assert.Null(device.ArchivedUtc); Assert.NotNull(device.RevokedUtc); Assert.Null(device.TokenHash);
    }

    [Fact]
    public async Task Archiving_preserves_files_but_disables_schedules_and_pending_publication_until_explicit_republish()
    {
        using var admin = await AdminAsync();
        var version = await app.SeedPresentationAsync("Old PowerPoint");
        await using (var db = await app.DbAsync())
        {
            db.PublishTargets.Add(new PublishTarget { PresentationVersionId = version.Id, ScreenGroupId = DevelopmentSeeder.ReceptionId, StartsUtc = DateTimeOffset.UtcNow, PublishWhenReady = true });
            await db.SaveChangesAsync();
        }
        await PostAsync(admin, "/Admin/Presentations", "Archive", ("presentationId", version.PresentationId.ToString()));
        Assert.DoesNotContain("Old PowerPoint", await admin.GetStringAsync("/Admin/Presentations"));
        Assert.Contains("Old PowerPoint", await admin.GetStringAsync("/Admin/Presentations?archived=true"));
        Assert.True(File.Exists(app.SourcePath(version))); Assert.True(Directory.Exists(app.PackagePath(version)));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var publishing = scope.ServiceProvider.GetRequiredService<PresentationService>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => publishing.PublishReadyAsync(version.Id, DevelopmentSeeder.ReceptionId, "dev-admin", default));
            var maintenance = scope.ServiceProvider.GetRequiredService<MaintenanceProcessor>();
            await maintenance.RunAsync(false, default);
        }
        Assert.True(File.Exists(app.SourcePath(version)));
        await PostAsync(admin, "/Admin/Presentations?archived=true", "Restore", ("presentationId", version.PresentationId.ToString()));
        await using var verify = await app.DbAsync();
        Assert.Null((await verify.Presentations.SingleAsync(item => item.Id == version.PresentationId)).ArchivedUtc);
        Assert.False((await verify.Publications.SingleAsync(item => item.PresentationVersionId == version.Id)).IsEnabled);
        Assert.False((await verify.PublishTargets.SingleAsync(item => item.PresentationVersionId == version.Id)).PublishWhenReady);
    }

    [Fact]
    public async Task Deletion_removes_versions_schedules_jobs_and_files_and_requires_confirmation()
    {
        using var admin = await AdminAsync();
        var version = await app.SeedPresentationAsync("Delete me");
        await using (var db = await app.DbAsync())
        {
            db.ConversionJobs.Add(new ConversionJob { Id = Guid.NewGuid(), PresentationVersionId = version.Id, Status = ConversionJobStatus.Queued, CreatedUtc = DateTimeOffset.UtcNow, AvailableUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(admin, "/Admin/Presentations", "Delete", ("presentationId", version.PresentationId.ToString()))).StatusCode);
        await PostAsync(admin, "/Admin/Presentations", "Delete", ("presentationId", version.PresentationId.ToString()), ("confirmed", "true"));
        await using var verify = await app.DbAsync();
        Assert.False(await verify.Presentations.AnyAsync(item => item.Id == version.PresentationId));
        Assert.False(await verify.PresentationVersions.AnyAsync(item => item.Id == version.Id));
        Assert.False(await verify.Publications.AnyAsync(item => item.PresentationVersionId == version.Id));
        Assert.False(await verify.ConversionJobs.AnyAsync(item => item.PresentationVersionId == version.Id));
        Assert.False(File.Exists(app.SourcePath(version))); Assert.False(Directory.Exists(app.PackagePath(version)));
        Assert.Empty(await verify.PendingContentDeletions.ToListAsync());
    }

    [Fact]
    public async Task Deletion_keeps_shared_files_and_retries_a_locked_source()
    {
        using var admin = await AdminAsync();
        var first = await app.SeedPresentationAsync("Shared first");
        var second = await app.SeedPresentationAsync("Shared second");
        await using (var db = await app.DbAsync())
        {
            var shared = await db.PresentationVersions.SingleAsync(item => item.Id == second.Id);
            shared.SourceStorageKey = first.SourceStorageKey; shared.ContentId = first.ContentId;
            await db.SaveChangesAsync();
        }
        await PostAsync(admin, "/Admin/Presentations", "Delete", ("presentationId", first.PresentationId.ToString()), ("confirmed", "true"));
        Assert.True(File.Exists(app.SourcePath(first))); Assert.True(Directory.Exists(app.PackagePath(first)));
        using (var locked = new FileStream(app.SourcePath(first), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await PostAsync(admin, "/Admin/Presentations", "Delete", ("presentationId", second.PresentationId.ToString()), ("confirmed", "true"));
            await using var db = await app.DbAsync();
            Assert.Single(await db.PendingContentDeletions.ToListAsync());
        }
        await using (var scope = app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<ContentDeletionProcessor>().PurgeAsync(default);
        Assert.False(File.Exists(app.SourcePath(first))); Assert.False(Directory.Exists(app.PackagePath(first)));
    }

    [Fact]
    public async Task Deleting_a_presentation_during_conversion_is_rejected_without_removing_its_files()
    {
        using var admin = await AdminAsync();
        var version = await app.SeedPresentationAsync("Converting item");
        await using (var db = await app.DbAsync())
        {
            db.ConversionJobs.Add(new ConversionJob { Id = Guid.NewGuid(), PresentationVersionId = version.Id, Status = ConversionJobStatus.Processing, CreatedUtc = DateTimeOffset.UtcNow, AvailableUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await PostAsync(admin, "/Admin/Presentations", "Delete", ("presentationId", version.PresentationId.ToString()), ("confirmed", "true"));
        Assert.Contains("Wait for conversion to finish", await admin.GetStringAsync("/Admin/Presentations"));
        Assert.True(File.Exists(app.SourcePath(version)));
        await using var verify = await app.DbAsync(); Assert.True(await verify.Presentations.AnyAsync(item => item.Id == version.PresentationId));
    }

    [Fact]
    public async Task Folders_are_unique_can_be_renamed_and_removed_without_deleting_their_presentations()
    {
        using var admin = await AdminAsync();
        var version = await app.SeedPresentationAsync("Organise this");
        await PostAsync(admin, "/Admin/Presentations", "CreateFolder", ("folderName", "Assemblies"));
        Guid folderId;
        await using (var db = await app.DbAsync()) folderId = (await db.PresentationFolders.SingleAsync()).Id;
        await PostAsync(admin, "/Admin/Presentations", "CreateFolder", ("folderName", " assemblies "));
        Assert.Contains("already exists", await admin.GetStringAsync("/Admin/Presentations"));
        await PostAsync(admin, "/Admin/Presentations", "Move", ("presentationId", version.PresentationId.ToString()), ("targetFolderId", folderId.ToString()));
        Assert.Contains("Organise this", await admin.GetStringAsync($"/Admin/Presentations?folderId={folderId}"));
        Assert.DoesNotContain("Organise this", await admin.GetStringAsync("/Admin/Presentations?unfiled=true"));
        await PostAsync(admin, "/Admin/Presentations", "RenameFolder", ("id", folderId.ToString()), ("folderName", "School assemblies"));
        Assert.Contains("School assemblies", await admin.GetStringAsync("/Admin/Presentations"));
        await PostAsync(admin, "/Admin/Presentations", "DeleteFolder", ("id", folderId.ToString()), ("confirmed", "true"));
        await using var verify = await app.DbAsync();
        Assert.Null((await verify.Presentations.SingleAsync(item => item.Id == version.PresentationId)).FolderId);
        Assert.Empty(await verify.PresentationFolders.ToListAsync());
        Assert.True(File.Exists(app.SourcePath(version)));
    }

    [Fact]
    public async Task Management_posts_require_administrator_access_and_a_csrf_token()
    {
        using var teacher = app.Client(); await teacher.GetAsync("/dev-login?asRole=teacher");
        foreach (var page in new[] { "/Admin/Devices", "/Admin/Presentations" })
        {
            using var get = await teacher.GetAsync(page);
            Assert.Equal(HttpStatusCode.Found, get.StatusCode);
            Assert.Contains("access-denied", get.Headers.Location!.ToString());
            using var post = await teacher.PostAsync(page + "?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["confirmed"] = "true" }));
            Assert.Equal(HttpStatusCode.Found, post.StatusCode);
            Assert.Contains("access-denied", post.Headers.Location!.ToString());
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/admin/devices")).StatusCode);
        using var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/Admin/Presentations?handler=CreateFolder", new FormUrlEncodedContent(new Dictionary<string, string> { ["folderName"] = "No token" }))).StatusCode);
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = app.Client(); await client.GetAsync("/dev-login?asRole=admin"); return client;
    }
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string page, string handler, params (string Name, string Value)[] values)
    {
        var html = await client.GetStringAsync(page);
        var csrf = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(csrf);
        var data = values.Select(value => new KeyValuePair<string, string>(value.Name, value.Value)).ToList();
        data.Add(new("__RequestVerificationToken", csrf));
        return await client.PostAsync(page + (page.Contains('?') ? "&" : "?") + "handler=" + handler, new FormUrlEncodedContent(data));
    }
    public void Dispose() => app.Dispose();

    private sealed class LibraryApplication : WebApplicationFactory<Program>
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"signage-library-tests-{Guid.NewGuid():N}");
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        public Task<SignageDbContext> DbAsync() => Services.GetRequiredService<IDbContextFactory<SignageDbContext>>().CreateDbContextAsync();
        public string SourcePath(PresentationVersion version) => Path.Combine(Root, "content", version.SourceStorageKey);
        public string PackagePath(PresentationVersion version) => Path.Combine(Root, "content", "packages", version.ContentId!);
        public async Task<PresentationVersion> SeedPresentationAsync(string name)
        {
            await using var db = await DbAsync();
            var presentation = new Presentation { Id = Guid.NewGuid(), Name = name, CreatedBySubject = "dev-admin", CreatedUtc = DateTimeOffset.UtcNow };
            var version = new PresentationVersion { Id = Guid.NewGuid(), PresentationId = presentation.Id, VersionNumber = 1, OriginalFileName = name + ".pptx", SourceStorageKey = "", SourceSha256 = "", Status = PresentationVersionStatus.Ready, CreatedUtc = DateTimeOffset.UtcNow, ContentId = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), SlideCount = 1 };
            var storage = Services.GetRequiredService<IContentStorage>();
            var stored = await storage.SaveSourceAsync(new MemoryStream([80, 75, 3, 4]), version.Id, default);
            version.SourceStorageKey = stored.Key; version.SourceSha256 = stored.Sha256; presentation.CurrentVersionId = version.Id;
            Directory.CreateDirectory(PackagePath(version)); await File.WriteAllTextAsync(Path.Combine(PackagePath(version), "asset.txt"), "test package");
            db.Presentations.Add(presentation); db.PresentationVersions.Add(version);
            db.Publications.Add(new Publication { Id = Guid.NewGuid(), PresentationVersionId = version.Id, ScreenGroupId = DevelopmentSeeder.ReceptionId, StartsUtc = DateTimeOffset.UtcNow, PublishedUtc = DateTimeOffset.UtcNow, PublishedBySubject = "dev-admin", IsEnabled = true });
            await db.SaveChangesAsync(); return version;
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(Root); builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SignageDb"] = $"Data Source={Path.Combine(Root, "signage.db")};Foreign Keys=True;Default Timeout=5",
                ["Database:InstanceLockPath"] = Path.Combine(Root, "signage.lock"), ["Storage:RootPath"] = Path.Combine(Root, "content"), ["Storage:MinimumFreeBytes"] = "0",
                ["Rendering:TempRoot"] = Path.Combine(Root, "temp"), ["Rendering:FontDirectories:0"] = null, ["Authentication:Mode"] = "Development"
            }));
            builder.ConfigureServices(services =>
            {
                foreach (var descriptor in services.Where(item => item.ServiceType == typeof(IHostedService) && (item.ImplementationType == typeof(ConversionWorker) || item.ImplementationType == typeof(MaintenanceWorker))).ToList()) services.Remove(descriptor);
            });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing); SqliteConnection.ClearAllPools(); TestDataCleanup.DeleteDirectory(Root);
        }
    }
}
