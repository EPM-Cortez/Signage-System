using Microsoft.EntityFrameworkCore;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Services;

public sealed class DeviceManagementService(IDbContextFactory<SignageDbContext> dbFactory, TimeProvider timeProvider)
{
    public async Task ArchiveAsync(Guid id, bool archive, string actor, CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        var device = await db.Devices.SingleOrDefaultAsync(item => item.Id == id, token)
            ?? throw new InvalidOperationException("The device was not found.");
        device.ArchivedUtc = archive ? timeProvider.GetUtcNow() : null;
        db.AuditEvents.Add(Audit(actor, archive ? "DeviceArchived" : "DeviceRestored", device));
        await db.SaveChangesAsync(token);
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken token)
    {
        await using var db = await dbFactory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var device = await db.Devices.SingleOrDefaultAsync(item => item.Id == id, token)
            ?? throw new InvalidOperationException("The device was not found.");
        var pairings = await db.PairingSessions.Where(item => item.ApprovedDeviceId == id).ToListAsync(token);
        db.PairingSessions.RemoveRange(pairings);
        db.Devices.Remove(device);
        db.AuditEvents.Add(Audit(actor, "DeviceDeleted", device));
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    private AuditEvent Audit(string actor, string action, Device device) => new()
    {
        Id = Guid.NewGuid(), OccurredUtc = timeProvider.GetUtcNow(), ActorType = "Human", ActorId = actor,
        Action = action, EntityType = nameof(Device), EntityId = device.Id.ToString(), Summary = $"{action[6..]} device {device.Name}."
    };
}
