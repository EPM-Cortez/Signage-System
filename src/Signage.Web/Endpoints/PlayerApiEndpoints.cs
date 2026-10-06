using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;
using Signage.Web.Services;

namespace Signage.Web.Endpoints;

public static class PlayerApiEndpoints
{
    public static IEndpointRouteBuilder MapPlayerApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/player");
        group.MapPost("/pairing-sessions", async (PairingService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateAsync(cancellationToken))).RequireRateLimiting("pairing");

        group.MapGet("/pairing-sessions/{sessionId:guid}", async (
            Guid sessionId,
            HttpContext context,
            PairingService service,
            CancellationToken cancellationToken) =>
        {
            var temporaryToken = EndpointUtilities.Bearer(context);
            if (temporaryToken is null)
            {
                return Results.Unauthorized();
            }
            var result = await service.PollAsync(sessionId, temporaryToken, cancellationToken);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).RequireRateLimiting("pairing");

        group.MapGet("/assignment", async (
            HttpContext context,
            IDbContextFactory<SignageDbContext> dbFactory,
            IOptions<SignageOptions> signageOptions,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var device = await EndpointUtilities.AuthenticateDeviceAsync(context, dbFactory, cancellationToken, allowArchived: true);
            // A temporary archive must not make the player discard its pairing as it does for a revoked token (401).
            if (device?.ArchivedUtc is not null) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (device?.ScreenGroupId is null)
            {
                return Results.Unauthorized();
            }
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            var publications = await db.Publications.AsNoTracking()
                .Include(item => item.PresentationVersion)
                .Where(item => item.ScreenGroupId == device.ScreenGroupId && item.IsEnabled && item.PresentationVersion.Presentation.ArchivedUtc == null)
                .ToListAsync(cancellationToken);
            var active = PublicationRules.SelectActive(publications, now);
            var next = publications.Where(item => item.StartsUtc > now).OrderBy(item => item.StartsUtc).FirstOrDefault();
            context.Response.Headers.CacheControl = "no-store";
            if (active?.PresentationVersion.ContentId is null)
            {
                return Results.Ok(new
                {
                    publication = (object?)null,
                    serverUtc = now,
                    pollIntervalSeconds = signageOptions.Value.PlayerPollSeconds,
                    nextScheduled = next is null ? null : new { next.StartsUtc }
                });
            }
            var etag = $"\"{active.PresentationVersion.ContentId}\"";
            if (context.Request.Headers.IfNoneMatch.Any(value => string.Equals(value, etag, StringComparison.Ordinal)))
            {
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }
            context.Response.Headers.ETag = etag;
            return Results.Ok(new
            {
                publication = new { active.Id, active.PresentationVersionId, active.StartsUtc },
                contentId = active.PresentationVersion.ContentId,
                manifestUrl = $"/content/{active.PresentationVersion.ContentId}/manifest.json",
                manifestEtag = etag,
                activationTime = active.StartsUtc,
                nextScheduled = next is null ? null : new
                {
                    next.StartsUtc,
                    next.PresentationVersionId,
                    manifestUrl = next.PresentationVersion.ContentId is null ? null : $"/content/{next.PresentationVersion.ContentId}/manifest.json"
                },
                serverUtc = now,
                pollIntervalSeconds = signageOptions.Value.PlayerPollSeconds
            });
        }).RequireRateLimiting("player");

        group.MapPost("/heartbeat", async (
            PlayerHeartbeat heartbeat,
            HttpContext context,
            IDbContextFactory<SignageDbContext> dbFactory,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var authenticated = await EndpointUtilities.AuthenticateDeviceAsync(context, dbFactory, cancellationToken);
            if (authenticated is null)
            {
                return Results.Unauthorized();
            }
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var device = await db.Devices.SingleAsync(item => item.Id == authenticated.Id, cancellationToken);
            device.LastSeenUtc = timeProvider.GetUtcNow();
            device.LastIpAddress = context.Connection.RemoteIpAddress?.ToString();
            device.BrowserSummary = Truncate(heartbeat.BrowserSummary, 500);
            device.PlayingContentId = Truncate(heartbeat.ContentId, 128);
            device.PlayingPresentationVersionId = heartbeat.PresentationVersionId;
            device.LastError = Truncate(heartbeat.LastError, 1000);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("player");
        return app;
    }

    private static string? Truncate(string? value, int length) => value is null || value.Length <= length ? value : value[..length];

    public sealed record PlayerHeartbeat(
        string? ContentId,
        Guid? PresentationVersionId,
        int CurrentItemIndex,
        string PlayerVersion,
        string BrowserSummary,
        string? LastError,
        StorageEstimate? StorageEstimate);

    public sealed record StorageEstimate(long? UsageBytes, long? QuotaBytes);
}
