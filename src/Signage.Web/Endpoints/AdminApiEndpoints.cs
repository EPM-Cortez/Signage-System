using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Infrastructure.Persistence;
using Signage.Web.Services;

namespace Signage.Web.Endpoints;

public static class AdminApiEndpoints
{
    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization("SignageAdmin");
        group.MapGet("/devices", async (
            IDbContextFactory<SignageDbContext> dbFactory,
            IOptions<SignageOptions> signageOptions,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var offlineBefore = timeProvider.GetUtcNow().AddSeconds(-signageOptions.Value.OfflineAfterSeconds);
            var devices = await db.Devices.AsNoTracking().Include(item => item.ScreenGroup).OrderBy(item => item.Name).ToListAsync(cancellationToken);
            return Results.Ok(devices.Select(item => new
            {
                item.Id,
                item.Name,
                screenGroup = item.ScreenGroup?.Name,
                item.LastSeenUtc,
                online = item.RevokedUtc is null && item.LastSeenUtc >= offlineBefore,
                revoked = item.RevokedUtc is not null,
                item.PlayingContentId,
                item.PlayingPresentationVersionId,
                item.LastError
            }));
        });

        group.MapPost("/devices/{deviceId:guid}/revoke", async (
            Guid deviceId,
            HttpContext context,
            IAntiforgery antiforgery,
            IDbContextFactory<SignageDbContext> dbFactory,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var device = await db.Devices.SingleOrDefaultAsync(item => item.Id == deviceId, cancellationToken);
            if (device is null) return Results.NotFound();
            device.RevokedUtc = timeProvider.GetUtcNow();
            device.TokenHash = null;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/devices/{deviceId:guid}/move", async (
            Guid deviceId,
            MoveDeviceRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            IDbContextFactory<SignageDbContext> dbFactory,
            CancellationToken cancellationToken) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var device = await db.Devices.SingleOrDefaultAsync(item => item.Id == deviceId, cancellationToken);
            if (device is null) return Results.NotFound();
            if (!await db.ScreenGroups.AnyAsync(item => item.Id == request.ScreenGroupId && !item.IsArchived, cancellationToken)) return Results.BadRequest();
            device.ScreenGroupId = request.ScreenGroupId;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/pairing/{code}/approve", async (
            string code,
            ApprovePairingRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            PairingService service,
            CancellationToken cancellationToken) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var id = await service.ApproveAsync(code, request.DeviceName, request.ScreenGroupId, EndpointUtilities.Subject(context.User), cancellationToken);
            return Results.Ok(new { deviceId = id });
        });

        group.MapPost("/presentation-versions/{versionId:guid}/retry", async (
            Guid versionId,
            HttpContext context,
            IAntiforgery antiforgery,
            PresentationService service,
            CancellationToken cancellationToken) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            await service.RetryAsync(versionId, EndpointUtilities.Subject(context.User), cancellationToken);
            return Results.Accepted();
        });

        group.MapPost("/presentation-versions/{versionId:guid}/rollback", async (
            Guid versionId,
            RollbackRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            PresentationService service,
            CancellationToken cancellationToken) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            await service.PublishReadyAsync(versionId, request.ScreenGroupId, EndpointUtilities.Subject(context.User), cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/audit", async (IDbContextFactory<SignageDbContext> dbFactory, CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return Results.Ok(await db.AuditEvents.AsNoTracking().OrderByDescending(item => item.OccurredUtc).Take(250).ToListAsync(cancellationToken));
        });

        group.MapGet("/system-status", async (
            IDbContextFactory<SignageDbContext> dbFactory,
            IContentStorage storage,
            IConverterRunner converter,
            CancellationToken cancellationToken) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return Results.Ok(new
            {
                database = await db.Database.CanConnectAsync(cancellationToken),
                storageWritable = await storage.CanWriteAsync(cancellationToken),
                storageAvailableBytes = storage.GetAvailableBytes(),
                converter = await converter.CheckHealthAsync(cancellationToken),
                queuedJobs = await db.ConversionJobs.CountAsync(item => item.Status == Domain.ConversionJobStatus.Queued, cancellationToken),
                failedVersions = await db.PresentationVersions.CountAsync(item => item.Status == Domain.PresentationVersionStatus.Failed, cancellationToken)
            });
        });
        return app;
    }

    public sealed record MoveDeviceRequest(Guid ScreenGroupId);
    public sealed record ApprovePairingRequest(string DeviceName, Guid ScreenGroupId);
    public sealed record RollbackRequest(Guid ScreenGroupId);
}
