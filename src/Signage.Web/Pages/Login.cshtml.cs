using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Signage.Domain;
using Signage.Web.Services;
using AuthenticationOptions = Signage.Application.AuthenticationOptions;

namespace Signage.Web.Pages;

[AllowAnonymous]
[EnableRateLimiting("directory-login")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(16_384)]
public sealed class LoginModel(IActiveDirectoryAuthenticator directory, StaffAccessService staff, IOptions<AuthenticationOptions> options) : PageModel
{
    [BindProperty] public CredentialInput Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string Mode => options.Value.Mode;
    public bool CanSubmit => Mode.Equals("ActiveDirectory", StringComparison.OrdinalIgnoreCase) && Request.IsHttps;

    public IActionResult OnGet()
    {
        if (Mode.Equals("OpenIdConnect", StringComparison.OrdinalIgnoreCase))
            return Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl("/") }, OpenIdConnectDefaults.AuthenticationScheme);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken token)
    {
        try
        {
            if (!CanSubmit)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                ModelState.AddModelError(string.Empty, "Active Directory sign-in requires an HTTPS address and configured directory authentication.");
                return Page();
            }
            if (!ModelState.IsValid) return Page();
            var identity = await directory.AuthenticateAsync(Input.Username, Input.Password, token);
            var account = identity is null ? null : await staff.SignInDirectoryAsync(identity, token);
            if (account is null)
            {
                ModelState.AddModelError(string.Empty, "Sign-in failed. Check your school username and password, or contact IT.");
                return Page();
            }
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, StaffAccessService.CreatePrincipal(account),
                new AuthenticationProperties { IsPersistent = false });
            var destination = account.Role == StaffRole.Administrator ? "/Admin" : account.Role == StaffRole.Teacher ? "/Publish" : "/AccessPending";
            return LocalRedirect(account.Role == StaffRole.Pending ? "/AccessPending" : SafeReturnUrl(destination));
        }
        catch (DirectoryUnavailableException exception) { ModelState.AddModelError(string.Empty, exception.Message); return Page(); }
        catch (ArgumentException) { ModelState.AddModelError(string.Empty, "Use your school username or full school sign-in name."); return Page(); }
        finally
        {
            Input.Password = string.Empty;
            ModelState.SetModelValue("Input.Password", string.Empty, string.Empty);
        }
    }

    private string SafeReturnUrl(string fallback) => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : fallback;
}

public sealed class CredentialInput
{
    [Required, StringLength(300)] public string Username { get; set; } = string.Empty;
    [Required, StringLength(1024), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
}
