using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Signage.Web.Pages;

[Authorize(Policy = "Teacher")]
public sealed class StatusModel : PageModel
{
    public Guid VersionId { get; private set; }
    public void OnGet(Guid versionId) => VersionId = versionId;
}
