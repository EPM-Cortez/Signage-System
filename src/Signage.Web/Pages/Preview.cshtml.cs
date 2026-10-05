using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Signage.Application;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Pages;

[Authorize(Policy = "Teacher")]
public sealed class PreviewModel(IDbContextFactory<SignageDbContext> dbFactory, IContentStorage storage) : PageModel
{
    public Guid VersionId { get; private set; }
    public string PresentationName { get; private set; } = string.Empty;
    public ContentManifest Manifest { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync(Guid versionId, CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        var version = await db.PresentationVersions.AsNoTracking().Include(item => item.Presentation).SingleOrDefaultAsync(item => item.Id == versionId, token);
        if (version?.ContentId is null) return NotFound();
        var subject = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!User.IsInRole("SignageAdmin") && version.Presentation.CreatedBySubject != subject) return Forbid();
        var manifestPath = Path.Combine(storage.GetPackagePath(version.ContentId), "manifest.json");
        Manifest = JsonSerializer.Deserialize<ContentManifest>(await System.IO.File.ReadAllTextAsync(manifestPath, token), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Content manifest is invalid.");
        VersionId = versionId; PresentationName = version.Presentation.Name; return Page();
    }
}
