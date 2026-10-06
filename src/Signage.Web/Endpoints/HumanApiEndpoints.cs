using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Infrastructure.Persistence;
using Signage.Web.Services;

namespace Signage.Web.Endpoints;

public static class HumanApiEndpoints
{
    public static IEndpointRouteBuilder MapHumanApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization("Teacher");
        group.MapGet("/screen-groups/available", async (HttpContext context, PresentationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAvailableGroupsAsync(
                EndpointUtilities.Subject(context.User),
                context.User.IsInRole("SignageAdmin"),
                cancellationToken)));

        group.MapPost("/presentations", async (
            HttpContext context,
            IAntiforgery antiforgery,
            PresentationService service,
            CancellationToken cancellationToken) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null)
            {
                return Results.BadRequest(new { error = "A PowerPoint file is required." });
            }
            var groupIds = form["screenGroupIds"].Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToArray();
            await using var stream = file.OpenReadStream();
            try
            {
            var versionId = await service.UploadAsync(
                EndpointUtilities.Subject(context.User),
                context.User.IsInRole("SignageAdmin"),
                form["name"].FirstOrDefault(),
                file.FileName,
                file.Length,
                stream,
                groupIds,
                !string.Equals(form["publishWhenReady"], "false", StringComparison.OrdinalIgnoreCase),
                null,
                null,
                cancellationToken);
            return Results.Accepted($"/api/presentation-versions/{versionId}/status", new { versionId, status = "Preparing for screens" });
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (PresentationRejectedException exception) { return Results.BadRequest(new { error = exception.Message, code = exception.Code }); }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        });

        group.MapGet("/presentation-versions/{versionId:guid}/status", async (
            Guid versionId,
            HttpContext context,
            IDbContextFactory<SignageDbContext> dbFactory,
            ConversionProgressTracker progressTracker,
            IOptions<SignageOptions> signageOptions,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var version = await db.PresentationVersions.AsNoTracking()
                .Include(item => item.Presentation)
                .SingleOrDefaultAsync(item => item.Id == versionId, cancellationToken);
            if (version is null)
            {
                return Results.NotFound();
            }
            var subject = EndpointUtilities.Subject(context.User);
            if (!context.User.IsInRole("SignageAdmin") && version.Presentation.CreatedBySubject != subject)
            {
                return Results.Forbid();
            }
            var publicationCount = version.Status == Domain.PresentationVersionStatus.Ready
                ? await db.Publications.Where(item => item.PresentationVersionId == version.Id && item.IsEnabled).Select(item => item.ScreenGroupId).Distinct().CountAsync(cancellationToken) : 0;
            var targetCount = await db.PublishTargets.CountAsync(item => item.PresentationVersionId == version.Id && item.PublishWhenReady, cancellationToken);
            var published = publicationCount > 0;
            var friendly = version.Status switch
            {
                Domain.PresentationVersionStatus.Ready => published ? "Published" : "Prepared for preview",
                Domain.PresentationVersionStatus.Failed => "Could not be prepared",
                _ => "Preparing for screens"
            };
            return Results.Ok(new
            {
                version.Id,
                status = friendly,
                ready = version.Status == Domain.PresentationVersionStatus.Ready,
                published,
                failed = version.Status == Domain.PresentationVersionStatus.Failed,
                message = version.Status == Domain.PresentationVersionStatus.Failed
                    ? "We could not prepare this PowerPoint for the screens. The content already playing has not been changed. IT can see the technical details."
                    : version.Status == Domain.PresentationVersionStatus.Ready
                        ? !published ? "The presentation is prepared but has not been published. Ask IT to check your screen-group permissions or publish settings."
                            : publicationCount < targetCount ? $"Published to {publicationCount} of {targetCount} selected screen groups. Some group permissions changed during preparation; ask IT to review the skipped targets."
                                : null
                        : null,
                previewUrl = version.ContentId is null ? null : $"/Preview/{version.Id}",
                progress = DescribeProgress(version, progressTracker),
                screens = published ? await DescribeScreensAsync(db, version.Id, timeProvider.GetUtcNow(), signageOptions.Value, cancellationToken) : null
            });
        });

        group.MapGet("/presentation-versions/{versionId:guid}/preview/{**assetPath}", async (
            Guid versionId,
            string assetPath,
            HttpContext context,
            IDbContextFactory<SignageDbContext> dbFactory,
            IContentStorage storage,
            CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var version = await db.PresentationVersions.AsNoTracking().Include(item => item.Presentation)
                .SingleOrDefaultAsync(item => item.Id == versionId, cancellationToken);
            if (version?.ContentId is null) return Results.NotFound();
            if (!context.User.IsInRole("SignageAdmin") && version.Presentation.CreatedBySubject != EndpointUtilities.Subject(context.User)) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(assetPath) || assetPath.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(assetPath)) return Results.BadRequest();
            var root = storage.GetPackagePath(version.ContentId);
            var candidate = Path.GetFullPath(Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate)) return Results.NotFound();
            return Path.GetExtension(candidate).ToLowerInvariant() switch
            {
                ".png" => Results.File(candidate, "image/png"),
                ".mp4" => Results.File(candidate, "video/mp4", enableRangeProcessing: true),
                ".webm" => Results.File(candidate, "video/webm", enableRangeProcessing: true),
                _ => Results.NotFound()
            };
        });
        return app;
    }

    private static object? DescribeProgress(Domain.PresentationVersion version, ConversionProgressTracker tracker)
    {
        if (version.Status is not (Domain.PresentationVersionStatus.Uploaded or Domain.PresentationVersionStatus.Queued or Domain.PresentationVersionStatus.Converting))
        {
            return null;
        }
        var current = version.Status == Domain.PresentationVersionStatus.Converting ? tracker.Get(version.Id) : null;
        if (current is null)
        {
            // Queued, or converting in a process that has since restarted: nothing measurable yet.
            var waiting = version.Status == Domain.PresentationVersionStatus.Converting ? "Getting started"
                : version.FailureCode is null ? "Waiting to start" : "Retrying after a problem";
            return new { percent = (int?)null, nextPercent = (int?)null, stage = waiting, slidesRendered = 0, slideCount = 0 };
        }
        var stage = current.Stage switch
        {
            ConversionStage.Starting => "Opening the PowerPoint",
            ConversionStage.Rendering when current.SlidesRendered < current.SlideCount => $"Preparing slide {current.SlidesRendered + 1} of {current.SlideCount}",
            _ => "Packaging for the screens"
        };
        return new { percent = (int?)current.Percent, nextPercent = (int?)current.NextPercent, stage, slidesRendered = current.SlidesRendered, slideCount = current.SlideCount };
    }

    private static async Task<object> DescribeScreensAsync(
        SignageDbContext db,
        Guid versionId,
        DateTimeOffset now,
        SignageOptions options,
        CancellationToken cancellationToken)
    {
        var groupIds = await db.Publications
            .Where(item => item.PresentationVersionId == versionId && item.IsEnabled)
            .Select(item => item.ScreenGroupId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var publications = await db.Publications.AsNoTracking()
            .Where(item => groupIds.Contains(item.ScreenGroupId) && item.IsEnabled)
            .ToListAsync(cancellationToken);
        var own = publications.Where(item => item.PresentationVersionId == versionId).ToList();
        // A screen only switches where this version is its group's active publication (same rule as /api/player/assignment).
        var liveGroupIds = groupIds
            .Where(id => Domain.PublicationRules.SelectActive(publications.Where(item => item.ScreenGroupId == id), now)?.PresentationVersionId == versionId)
            .ToList();
        var scheduledGroups = groupIds.Except(liveGroupIds).Count(id => own.Any(item => item.ScreenGroupId == id && item.StartsUtc > now));
        var overriddenGroups = groupIds.Except(liveGroupIds).Count(id => own.Any(item => item.ScreenGroupId == id && item.StartsUtc <= now && (item.EndsUtc is null || item.EndsUtc > now)));
        var devices = await db.Devices.AsNoTracking()
            .Where(item => item.RevokedUtc == null && item.ArchivedUtc == null && item.ScreenGroupId != null && liveGroupIds.Contains(item.ScreenGroupId.Value))
            .Select(item => new { item.LastSeenUtc, item.PlayingPresentationVersionId })
            .ToListAsync(cancellationToken);
        var offlineBefore = now.AddSeconds(-options.OfflineAfterSeconds);
        var online = devices.Where(item => item.LastSeenUtc >= offlineBefore).ToList();
        return new
        {
            total = devices.Count,
            showing = online.Count(item => item.PlayingPresentationVersionId == versionId),
            offline = devices.Count - online.Count,
            liveGroups = liveGroupIds.Count,
            scheduledGroups,
            overriddenGroups,
            nextStartUtc = own.Where(item => item.StartsUtc > now).Select(item => (DateTimeOffset?)item.StartsUtc).Min(),
            checkSeconds = options.PlayerPollSeconds
        };
    }
}
