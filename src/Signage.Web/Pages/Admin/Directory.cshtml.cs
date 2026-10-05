using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Web.Services;
using AuthenticationOptions = Signage.Application.AuthenticationOptions;

namespace Signage.Web.Pages.Admin;

[Authorize(Policy = "SignageAdmin")]
[EnableRateLimiting("directory-login")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(16_384)]
public sealed class DirectoryModel(IActiveDirectoryAuthenticator directory, IOptions<ActiveDirectoryOptions> settings, IOptions<AuthenticationOptions> authentication) : PageModel
{
    [BindProperty] public Signage.Web.Pages.CredentialInput Input { get; set; } = new();
    public ActiveDirectoryOptions Settings => settings.Value;
    public string AuthenticationMode => authentication.Value.Mode;
    public bool CanTest => Settings.IsConfigured && Request.IsHttps;
    public string? Result { get; private set; }
    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken token)
    {
        try
        {
            if (!CanTest) { Response.StatusCode = 400; ModelState.AddModelError(string.Empty, "Configure LDAPS and open this page over HTTPS before testing a real password."); return Page(); }
            if (!ModelState.IsValid) return Page();
            var identity = await directory.AuthenticateAsync(Input.Username, Input.Password, token);
            if (identity is null) ModelState.AddModelError(string.Empty, "Sign-in failed. Check the school username and password, or contact IT.");
            else Result = $"Connection successful. Active Directory authenticated {identity.DisplayName} ({identity.LoginName}). No roles or screen permissions were changed.";
            return Page();
        }
        catch (DirectoryUnavailableException exception) { ModelState.AddModelError(string.Empty, exception.Message); return Page(); }
        catch (ArgumentException) { ModelState.AddModelError(string.Empty, "Use your school username or full school sign-in name."); return Page(); }
        finally { Input.Password = string.Empty; ModelState.SetModelValue("Input.Password", string.Empty, string.Empty); }
    }
}
