using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Domain;
using Signage.Application;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Pages.Admin;

[Authorize(Policy = "SignageAdmin")]
public sealed class IndexModel(IDbContextFactory<SignageDbContext> dbFactory, IOptions<SignageOptions> options, TimeProvider timeProvider) : PageModel
{
    public int OnlineDevices { get; private set; }
    public int OfflineDevices { get; private set; }
    public int ActiveGroups { get; private set; }
    public int FailedPreparations { get; private set; }
    public List<AttentionRow> Attention { get; } = [];
    public List<GroupRow> Groups { get; } = [];
    public IReadOnlyList<PresentationVersion> RecentVersions { get; private set; } = [];
    public DateTimeOffset Now { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        Now = timeProvider.GetUtcNow();
        var offlineBefore = Now.AddSeconds(-options.Value.OfflineAfterSeconds);
        var devices = await db.Devices.AsNoTracking().Where(item => item.RevokedUtc == null && item.ArchivedUtc == null).ToListAsync(cancellationToken);
        OnlineDevices = devices.Count(item => item.LastSeenUtc >= offlineBefore);
        OfflineDevices = devices.Count - OnlineDevices;
        ActiveGroups = await db.ScreenGroups.CountAsync(item => !item.IsArchived, cancellationToken);
        FailedPreparations = await db.PresentationVersions.CountAsync(item => item.Status == PresentationVersionStatus.Failed && item.Presentation.ArchivedUtc == null, cancellationToken);
        foreach (var device in devices.Where(item => item.LastSeenUtc < offlineBefore || item.LastSeenUtc is null).Take(5))
        {
            var detail = device.LastSeenUtc is null ? "Never seen online" : $"Offline since {Signage.Web.Ui.Display.DateTime(device.LastSeenUtc.Value)}";
            Attention.Add(new AttentionRow(AttentionKind.OfflineDevice, device.Name, detail, "Open devices", "/Admin/Devices"));
        }
        var failures = await db.PresentationVersions.AsNoTracking().Where(item => item.Status == PresentationVersionStatus.Failed && item.Presentation.ArchivedUtc == null).OrderByDescending(item => item.CreatedUtc).Take(5).ToListAsync(cancellationToken);
        Attention.AddRange(failures.Select(item => new AttentionRow(AttentionKind.FailedPreparation, item.OriginalFileName, "Could not be prepared", "View diagnostics", "/Admin/Presentations")));

        var groups = await db.ScreenGroups.AsNoTracking().Where(item => !item.IsArchived)
            .Include(item => item.Devices).Include(item => item.Publications).ThenInclude(item => item.PresentationVersion).ThenInclude(item => item.Presentation)
            .OrderBy(item => item.Name).ToListAsync(cancellationToken);
        foreach (var group in groups)
        {
            var active = PublicationRules.SelectActive(group.Publications, Now);
            var online = group.Devices.Count(item => item.RevokedUtc == null && item.ArchivedUtc == null && item.LastSeenUtc >= offlineBefore);
            var offline = group.Devices.Count(item => item.RevokedUtc == null && item.ArchivedUtc == null) - online;
            Groups.Add(new GroupRow(group.Id, group.Name, active?.PresentationVersion, online, offline));
        }

        RecentVersions = await db.PresentationVersions.AsNoTracking().Include(item => item.Presentation)
            .Where(item => item.Presentation.ArchivedUtc == null)
            .OrderByDescending(item => item.CreatedUtc).Take(6).ToListAsync(cancellationToken);
    }

    public enum AttentionKind { OfflineDevice, FailedPreparation }
    public sealed record AttentionRow(AttentionKind Kind, string Title, string Detail, string Action, string Url);
    public sealed record GroupRow(Guid Id, string Name, PresentationVersion? NowPlaying, int Online, int Offline)
    {
        public bool HasOffline => Offline > 0;
    }
}
