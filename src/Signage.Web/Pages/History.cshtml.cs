using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Pages;

[Authorize(Policy = "Teacher")]
public sealed class HistoryModel(IDbContextFactory<SignageDbContext> dbFactory) : PageModel
{
    public IReadOnlyList<PresentationVersion> Items { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var subject = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = db.PresentationVersions.AsNoTracking().Include(item => item.Presentation).Where(item => item.Presentation.ArchivedUtc == null);
        if (!User.IsInRole("SignageAdmin")) query = query.Where(item => item.Presentation.CreatedBySubject == subject);
        Items = await query.OrderByDescending(item => item.CreatedUtc).Take(100).ToListAsync(cancellationToken);
    }
    public string FriendlyStatus(PresentationVersionStatus status) => status switch
    {
        PresentationVersionStatus.Ready => "Published",
        PresentationVersionStatus.Failed => "Could not be prepared",
        _ => "Preparing for screens"
    };
}
