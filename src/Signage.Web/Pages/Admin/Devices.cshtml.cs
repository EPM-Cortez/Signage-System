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
public sealed class DevicesModel(IDbContextFactory<SignageDbContext> dbFactory, IOptions<SignageOptions> options, TimeProvider timeProvider) : PageModel
{
    public IReadOnlyList<Device> Devices { get; private set; } = [];
    public IReadOnlyList<ScreenGroup> Groups { get; private set; } = [];
    public IReadOnlyDictionary<Guid, PresentationVersion> PlayingVersions { get; private set; } = new Dictionary<Guid, PresentationVersion>();
    public DateTimeOffset Now => timeProvider.GetUtcNow();
    public DateTimeOffset OfflineBefore => timeProvider.GetUtcNow().AddSeconds(-options.Value.OfflineAfterSeconds);
    public string StateOf(Device device) => device.RevokedUtc is not null ? "revoked" : device.LastSeenUtc >= OfflineBefore ? "online" : "offline";
    public async Task OnGetAsync(CancellationToken token) => await LoadAsync(token);
    public async Task<IActionResult> OnPostMoveAsync(Guid deviceId, Guid? screenGroupId, CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        var device = await db.Devices.SingleAsync(item => item.Id == deviceId, token);
        if (screenGroupId is not null && !await db.ScreenGroups.AnyAsync(item => item.Id == screenGroupId && !item.IsArchived, token)) return BadRequest();
        device.ScreenGroupId = screenGroupId;
        db.AuditEvents.Add(Audit("DeviceMoved", device.Id, $"Moved {device.Name}."));
        await db.SaveChangesAsync(token); TempData["Message"] = "Device moved."; return RedirectToPage();
    }
    public async Task<IActionResult> OnPostRevokeAsync(Guid deviceId, CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        var device = await db.Devices.SingleAsync(item => item.Id == deviceId, token);
        device.RevokedUtc = timeProvider.GetUtcNow(); device.TokenHash = null;
        db.AuditEvents.Add(Audit("DeviceRevoked", device.Id, $"Revoked {device.Name}."));
        await db.SaveChangesAsync(token); TempData["Message"] = "Device revoked."; return RedirectToPage();
    }
    private async Task LoadAsync(CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        Devices = await db.Devices.AsNoTracking().OrderBy(item => item.Name).ToListAsync(token);
        Groups = await db.ScreenGroups.AsNoTracking().Where(item => !item.IsArchived).OrderBy(item => item.Name).ToListAsync(token);
        var playingIds = Devices.Where(item => item.PlayingPresentationVersionId is not null).Select(item => item.PlayingPresentationVersionId!.Value).Distinct().ToList();
        PlayingVersions = await db.PresentationVersions.AsNoTracking().Include(item => item.Presentation)
            .Where(item => playingIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, token);
    }
    private AuditEvent Audit(string action, Guid id, string summary) => new() { Id = Guid.NewGuid(), OccurredUtc = timeProvider.GetUtcNow(), ActorType = "Human", ActorId = User.FindFirstValue("sub")!, Action = action, EntityType = nameof(Device), EntityId = id.ToString(), Summary = summary };
}
