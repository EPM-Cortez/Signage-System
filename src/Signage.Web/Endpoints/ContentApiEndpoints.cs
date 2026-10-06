using Microsoft.EntityFrameworkCore;
using Signage.Application;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Endpoints;

public static class ContentApiEndpoints
{
    public static IEndpointRouteBuilder MapContentApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/content/{contentId}/{**assetPath}", async (
            string contentId,
            string assetPath,
            HttpContext context,
            IDbContextFactory<SignageDbContext> dbFactory,
            IContentStorage storage,
            CancellationToken cancellationToken) =>
        {
            var device = await EndpointUtilities.AuthenticateDeviceAsync(context, dbFactory, cancellationToken);
            if (device?.ScreenGroupId is null)
            {
                return Results.Unauthorized();
            }
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var authorized = await db.Publications.AnyAsync(item =>
                item.ScreenGroupId == device.ScreenGroupId && item.IsEnabled && item.PresentationVersion.ContentId == contentId && item.PresentationVersion.Presentation.ArchivedUtc == null,
                cancellationToken);
            if (!authorized)
            {
                return Results.Forbid();
            }
            if (string.IsNullOrWhiteSpace(assetPath) || assetPath.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(assetPath))
            {
                return Results.BadRequest();
            }
            var packageRoot = storage.GetPackagePath(contentId);
            var candidate = Path.GetFullPath(Path.Combine(packageRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = packageRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
            {
                return Results.NotFound();
            }
            var extension = Path.GetExtension(candidate).ToLowerInvariant();
            var contentType = extension switch
            {
                ".json" => "application/json",
                ".png" => "image/png",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                _ => null
            };
            if (contentType is null)
            {
                return Results.NotFound();
            }
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(candidate, contentType, enableRangeProcessing: extension is ".mp4" or ".webm");
        }).RequireRateLimiting("player");
        return app;
    }
}
