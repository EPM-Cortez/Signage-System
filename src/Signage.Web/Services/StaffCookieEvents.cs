using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using AuthenticationOptions = Signage.Application.AuthenticationOptions;

namespace Signage.Web.Services;

public sealed class StaffCookieEvents(StaffAccessService staff, IOptions<AuthenticationOptions> options) : CookieAuthenticationEvents
{
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; }
        return base.RedirectToLogin(context);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; }
        return base.RedirectToAccessDenied(context);
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var subject = context.Principal?.FindFirstValue("sub");
        var account = subject is null ? null : await staff.FindAsync(subject, context.HttpContext.RequestAborted);
        if (account is null || !account.IsEnabled || !account.Provider.Equals(options.Value.Mode, StringComparison.OrdinalIgnoreCase)
            || context.Principal?.FindFirstValue("signage:provider") != account.Provider)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        var refreshed = StaffAccessService.CreatePrincipal(account);
        var existing = context.Principal!.Claims.Select(claim => (claim.Type, claim.Value));
        var current = refreshed.Claims.Select(claim => (claim.Type, claim.Value));
        if (!existing.SequenceEqual(current))
        {
            context.ReplacePrincipal(refreshed);
            context.ShouldRenew = true;
        }
    }
}
