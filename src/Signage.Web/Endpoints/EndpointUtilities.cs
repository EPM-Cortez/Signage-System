using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Endpoints;

internal static class EndpointUtilities
{
    public static string Subject(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();

    public static string? Bearer(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? value[7..].Trim() : null;
    }

    public static async Task<Device?> AuthenticateDeviceAsync(
        HttpContext context,
        IDbContextFactory<SignageDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        var token = Bearer(context);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }
        var hash = TokenUtility.Hash(token);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);
        return device is not null && device.RevokedUtc is null && TokenUtility.FixedTimeMatches(token, device.TokenHash!) ? device : null;
    }
}
