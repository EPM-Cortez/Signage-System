using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Pages.Admin;

[Authorize(Policy = "SignageAdmin")]
public sealed class ScreenGroupsModel(IDbContextFactory<SignageDbContext> dbFactory, IOptions<SignageOptions> options, TimeProvider timeProvider) : PageModel
{
    public IReadOnlyList<GroupCard> Groups { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken token) => await LoadAsync(token);
    public async Task<IActionResult> OnPostCreateAsync(string name, string? description, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest();
        await using var db = await dbFactory.CreateDbContextAsync(token);
        var group = new ScreenGroup { Id = Guid.NewGuid(), Name = name.Trim(), Description = description?.Trim() ?? string.Empty, CreatedUtc = timeProvider.GetUtcNow() };
        db.ScreenGroups.Add(group); db.AuditEvents.Add(Audit("ScreenGroupCreated", group.Id, $"Created {group.Name}.")); await db.SaveChangesAsync(token);
        TempData["Message"] = "Screen group created."; return RedirectToPage();
    }
    public async Task<IActionResult> OnPostRenameAsync(Guid id, string name, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest();
        await using var db = await dbFactory.CreateDbContextAsync(token); var group = await db.ScreenGroups.SingleAsync(item => item.Id == id, token); group.Name = name.Trim(); db.AuditEvents.Add(Audit("ScreenGroupRenamed", id, $"Renamed screen group to {group.Name}.")); await db.SaveChangesAsync(token); TempData["Message"] = "Screen group renamed."; return RedirectToPage();
    }
    public async Task<IActionResult> OnPostArchiveAsync(Guid id, CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token); var group = await db.ScreenGroups.SingleAsync(item => item.Id == id, token); group.IsArchived = true; db.AuditEvents.Add(Audit("ScreenGroupArchived", id, $"Archived {group.Name}.")); await db.SaveChangesAsync(token); TempData["Message"] = "Screen group archived."; return RedirectToPage();
    }
    private async Task LoadAsync(CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        var groups = await db.ScreenGroups.AsNoTracking()
            .Include(item => item.Devices)
            .Include(item => item.Publications).ThenInclude(item => item.PresentationVersion).ThenInclude(item => item.Presentation)
            .OrderBy(item => item.Name).ToListAsync(token);
        var now = timeProvider.GetUtcNow();
        var offlineBefore = now.AddSeconds(-options.Value.OfflineAfterSeconds);
        Groups = groups.Select(group =>
        {
            var devices = group.Devices.Where(item => item.RevokedUtc == null && item.ArchivedUtc == null).ToList();
            var online = devices.Count(item => item.LastSeenUtc >= offlineBefore);
            return new GroupCard(group, PublicationRules.SelectActive(group.Publications, now)?.PresentationVersion, online, devices.Count - online);
        }).ToList();
    }
    private AuditEvent Audit(string action, Guid id, string summary) => new() { Id = Guid.NewGuid(), OccurredUtc = timeProvider.GetUtcNow(), ActorType = "Human", ActorId = User.FindFirstValue("sub")!, Action = action, EntityType = nameof(ScreenGroup), EntityId = id.ToString(), Summary = summary };

    public sealed record GroupCard(ScreenGroup Group, PresentationVersion? NowPlaying, int Online, int Offline);
}
