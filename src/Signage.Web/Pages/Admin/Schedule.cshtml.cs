using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Pages.Admin;

[Authorize(Policy = "SignageAdmin")]
public sealed class ScheduleModel(IDbContextFactory<SignageDbContext> dbFactory, TimeProvider timeProvider) : PageModel
{
    public const int WindowDays = 14;
    public IReadOnlyList<Publication> Publications { get; private set; } = [];
    public IReadOnlyList<GroupSchedule> Groups { get; private set; } = [];
    public IReadOnlyList<ScheduleRow> Past { get; private set; } = [];
    public IReadOnlyList<DateTime> Days { get; private set; } = [];
    public double NowPosition { get; private set; }

    public async Task OnGetAsync(CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        Publications = await db.Publications.AsNoTracking().Include(item => item.ScreenGroup).Include(item => item.PresentationVersion).ThenInclude(item => item.Presentation).OrderByDescending(item => item.PublishedUtc).Take(250).ToListAsync(token);
        var screenGroups = await db.ScreenGroups.AsNoTracking().Where(item => !item.IsArchived).OrderBy(item => item.Name).ToListAsync(token);

        var now = timeProvider.GetUtcNow();
        var today = now.ToLocalTime().Date;
        var windowStart = new DateTimeOffset(today, TimeZoneInfo.Local.GetUtcOffset(today));
        var windowEnd = windowStart.AddDays(WindowDays);
        double Position(DateTimeOffset value) => Math.Clamp((value - windowStart) / (windowEnd - windowStart), 0, 1);
        Days = Enumerable.Range(0, WindowDays).Select(offset => today.AddDays(offset)).ToList();
        NowPosition = Position(now);

        var rows = new List<ScheduleRow>();
        foreach (var group in Publications.GroupBy(item => item.ScreenGroupId))
        {
            var active = PublicationRules.SelectActive(group, now);
            foreach (var publication in group)
            {
                var state = !publication.IsEnabled ? ScheduleState.Unpublished
                    : publication.EndsUtc <= now ? ScheduleState.Ended
                    : publication.StartsUtc > now ? ScheduleState.Scheduled
                    : publication.Id == active?.Id ? ScheduleState.OnScreen
                    : ScheduleState.Standby;
                var barStart = publication.StartsUtc < windowStart ? windowStart : publication.StartsUtc;
                var barEnd = publication.EndsUtc is { } ends && ends < windowEnd ? ends : windowEnd;
                var visible = state is not (ScheduleState.Unpublished or ScheduleState.Ended) && barEnd > barStart;
                rows.Add(new ScheduleRow(
                    publication,
                    state,
                    visible ? Position(barStart) : null,
                    visible ? Position(barEnd) : null,
                    publication.EndsUtc is null || publication.EndsUtc > windowEnd));
            }
        }

        var activeGroupIds = screenGroups.Select(item => item.Id).ToHashSet();
        Groups = screenGroups.Select(group =>
        {
            var current = rows
                .Where(row => row.Publication.ScreenGroupId == group.Id && row.State is ScheduleState.OnScreen or ScheduleState.Scheduled or ScheduleState.Standby)
                .OrderBy(row => row.State)
                .ThenBy(row => row.State == ScheduleState.Scheduled ? row.Publication.StartsUtc : DateTimeOffset.MinValue)
                .ThenByDescending(row => row.Publication.Priority)
                .ThenByDescending(row => row.Publication.PublishedUtc)
                .ToList();
            return new GroupSchedule(group, current);
        }).ToList();
        Past = rows
            .Where(row => row.State is ScheduleState.Ended or ScheduleState.Unpublished || !activeGroupIds.Contains(row.Publication.ScreenGroupId))
            .OrderByDescending(row => row.Publication.EndsUtc ?? row.Publication.PublishedUtc)
            .ToList();
    }

    public async Task<IActionResult> OnPostDisableAsync(Guid id, CancellationToken token) { await using var db = await dbFactory.CreateDbContextAsync(token); var publication = await db.Publications.SingleAsync(item => item.Id == id, token); publication.IsEnabled = false; db.AuditEvents.Add(new AuditEvent { Id = Guid.NewGuid(), OccurredUtc = timeProvider.GetUtcNow(), ActorType = "Human", ActorId = User.FindFirstValue("sub")!, Action = "PublicationUnpublished", EntityType = nameof(Publication), EntityId = id.ToString(), Summary = "Unpublished a screen-group assignment." }); await db.SaveChangesAsync(token); return RedirectToPage(); }

    public enum ScheduleState { OnScreen, Scheduled, Standby, Ended, Unpublished }
    public sealed record ScheduleRow(Publication Publication, ScheduleState State, double? BarStart, double? BarEnd, bool OpenEnded);
    public sealed record GroupSchedule(ScreenGroup Group, IReadOnlyList<ScheduleRow> Rows)
    {
        public ScheduleRow? OnScreen => Rows.FirstOrDefault(row => row.State == ScheduleState.OnScreen);
    }
}
