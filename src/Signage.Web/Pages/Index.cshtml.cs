using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Signage.Web.Pages;

[Authorize]
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage(User.IsInRole("SignageAdmin") ? "/Admin/Index" : User.IsInRole("Teacher") ? "/Publish" : "/AccessPending");
}
