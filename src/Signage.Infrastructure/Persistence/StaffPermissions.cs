using Microsoft.EntityFrameworkCore;
using Signage.Domain;

namespace Signage.Infrastructure.Persistence;

public static class StaffPermissions
{
    public static async Task<HashSet<Guid>> AllowedGroupsAsync(SignageDbContext db, string subject, CancellationToken token)
    {
        var account = await db.StaffAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.IdentitySubject == subject, token);
        if (account is null || !account.IsEnabled || account.Role == StaffRole.Pending) return [];
        var groups = db.ScreenGroups.AsNoTracking().Where(item => !item.IsArchived);
        if (account.Role != StaffRole.Administrator)
            groups = groups.Where(group => db.PublisherAccess.Any(access => access.IdentitySubject == subject && access.ScreenGroupId == group.Id));
        return (await groups.Select(item => item.Id).ToListAsync(token)).ToHashSet();
    }
}
