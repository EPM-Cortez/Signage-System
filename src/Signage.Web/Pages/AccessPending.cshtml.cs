using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Signage.Web.Pages;

[Authorize]
public sealed class AccessPendingModel : PageModel
{
    public IActionResult OnGet() => User.IsInRole("SignageAdmin") ? RedirectToPage("/Admin/Index")
        : User.IsInRole("Teacher") ? RedirectToPage("/Publish") : Page();
}
