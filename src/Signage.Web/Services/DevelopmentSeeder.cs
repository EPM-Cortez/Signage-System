using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Services;

public sealed class DevelopmentSeeder(
    IDbContextFactory<SignageDbContext> dbFactory,
    IContentStorage storage,
    IWebHostEnvironment environment,
    IOptions<RenderingOptions> renderingOptions,
    IOptions<Signage.Application.AuthenticationOptions> authenticationOptions,
    TimeProvider timeProvider)
{
    public static readonly Guid ReceptionId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid StaffRoomId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (authenticationOptions.Value.Mode.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (subject, name, role) in new[] { ("dev-admin", "Sam Taylor", StaffRole.Administrator), ("dev-teacher", "Alex Morgan", StaffRole.Teacher) })
            {
                if (!await db.StaffAccounts.AnyAsync(item => item.IdentitySubject == subject, cancellationToken))
                    db.StaffAccounts.Add(new StaffAccount { Id = Guid.NewGuid(), IdentitySubject = subject, Provider = "Development", ExternalSubject = subject,
                        LoginName = subject, NormalizedLoginName = subject, DisplayName = name, Role = role, CreatedUtc = timeProvider.GetUtcNow() });
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        if (!await db.ScreenGroups.AnyAsync(cancellationToken))
        {
            db.ScreenGroups.AddRange(
                new ScreenGroup { Id = ReceptionId, Name = "Reception", Description = "Main reception screens", CreatedUtc = timeProvider.GetUtcNow() },
                new ScreenGroup { Id = StaffRoomId, Name = "Staff Room", Description = "Staff briefing screens", CreatedUtc = timeProvider.GetUtcNow() });
            db.PublisherAccess.Add(new PublisherAccess { IdentitySubject = "dev-teacher", ScreenGroupId = ReceptionId });
            await db.SaveChangesAsync(cancellationToken);
        }
        if (await db.PresentationVersions.AnyAsync(item => item.SourceStorageKey == "development-seed", cancellationToken))
        {
            return;
        }

        var assetSource = Path.Combine(environment.ContentRootPath, "SeedAssets", "welcome.png");
        if (!File.Exists(assetSource))
        {
            return;
        }
        var versionId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var presentationId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        var seedTempRoot = Path.GetFullPath(renderingOptions.Value.TempRoot, environment.ContentRootPath);
        Directory.CreateDirectory(seedTempRoot);
        var temporary = Path.Combine(seedTempRoot, $"signage-seed-{Guid.NewGuid():N}");
        var slides = Path.Combine(temporary, "slides");
        Directory.CreateDirectory(slides);
        var assetPath = Path.Combine(slides, "slide-0001.png");
        File.Copy(assetSource, assetPath);
        string assetHash;
        await using (var stream = File.OpenRead(assetPath))
        {
            assetHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        }
        var item = new ManifestItem("image", 1, "slides/slide-0001.png", assetHash, 10_000, new ManifestTransition("fade", 500));
        var contentId = TokenUtility.CalculateContentId(new { versionId, canvas = new { width = 1792, height = 1024 }, item });
        var manifest = new ContentManifest(1, contentId, versionId, timeProvider.GetUtcNow(), true, new ManifestCanvas(1792, 1024), [item]);
        await File.WriteAllTextAsync(Path.Combine(temporary, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }), cancellationToken);
        await storage.CommitPackageAsync(temporary, contentId, cancellationToken);

        db.Presentations.Add(new Presentation
        {
            Id = presentationId,
            Name = "Welcome presentation",
            CreatedBySubject = "dev-admin",
            CreatedUtc = timeProvider.GetUtcNow(),
            CurrentVersionId = versionId
        });
        db.PresentationVersions.Add(new PresentationVersion
        {
            Id = versionId,
            PresentationId = presentationId,
            VersionNumber = 1,
            OriginalFileName = "Welcome presentation.pptx",
            SourceStorageKey = "development-seed",
            SourceSha256 = assetHash,
            Status = PresentationVersionStatus.Ready,
            SlideCount = 1,
            TotalDurationMs = 10_000,
            ContentId = contentId,
            ManifestStorageKey = $"packages/{contentId}/manifest.json",
            CreatedUtc = timeProvider.GetUtcNow(),
            ReadyUtc = timeProvider.GetUtcNow()
        });
        db.Publications.Add(new Publication
        {
            Id = Guid.NewGuid(),
            ScreenGroupId = ReceptionId,
            PresentationVersionId = versionId,
            StartsUtc = timeProvider.GetUtcNow(),
            Priority = 0,
            IsEnabled = true,
            PublishedBySubject = "dev-admin",
            PublishedUtc = timeProvider.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
