using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Pages.Admin;

[Authorize(Policy = "SignageAdmin")]
public sealed class AuditModel(IDbContextFactory<SignageDbContext> dbFactory) : PageModel
{
    public IReadOnlyList<AuditEvent> Events { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken token) { await using var db = await dbFactory.CreateDbContextAsync(token); Events = await db.AuditEvents.AsNoTracking().OrderByDescending(item => item.OccurredUtc).Take(500).ToListAsync(token); }
}
