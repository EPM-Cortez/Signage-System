using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;

namespace Signage.Web.Services;

public sealed record PairingSessionResult(Guid SessionId, string TemporaryToken, string Code, DateTimeOffset ExpiresUtc);
public sealed record PairingPollResult(bool Paired, string? DeviceToken, string? DeviceName);

public sealed class PairingService(
    IDbContextFactory<SignageDbContext> dbFactory,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("SchoolSignage.PairingDeviceToken.v1");

    public async Task<PairingSessionResult> CreateAsync(CancellationToken cancellationToken)
    {
        var temporaryToken = TokenUtility.CreateToken();
        string code;
        string codeHash;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        do
        {
            code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            codeHash = TokenUtility.Hash(code);
        }
        while (await db.PairingSessions.AnyAsync(item => item.CodeHash == codeHash && item.ConsumedUtc == null, cancellationToken));

        var expires = timeProvider.GetUtcNow().AddMinutes(15);
        var session = new PairingSession
        {
            Id = Guid.NewGuid(),
            CodeHash = codeHash,
            TemporaryTokenHash = TokenUtility.Hash(temporaryToken),
            ExpiresUtc = expires
        };
        db.PairingSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return new PairingSessionResult(session.Id, temporaryToken, code, expires);
    }

    public async Task<PairingPollResult?> PollAsync(Guid sessionId, string temporaryToken, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.PairingSessions.Include(item => item.ApprovedDevice)
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
        if (session is null || !PairingRules.IsUsable(session, timeProvider.GetUtcNow()) ||
            !TokenUtility.FixedTimeMatches(temporaryToken, session.TemporaryTokenHash))
        {
            return null;
        }
        if (session.ApprovedDeviceId is null || session.ProtectedDeviceToken is null)
        {
            return new PairingPollResult(false, null, null);
        }
        if (session.ApprovedDevice is null || session.ApprovedDevice.ArchivedUtc is not null || session.ApprovedDevice.RevokedUtc is not null)
            return null;

        var rawToken = protector.Unprotect(session.ProtectedDeviceToken);
        session.ProtectedDeviceToken = null;
        session.ConsumedUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return new PairingPollResult(true, rawToken, session.ApprovedDevice?.Name);
    }

    public Task<Guid> ApproveAsync(
        string code,
        string deviceName,
        Guid screenGroupId,
        string actor,
        CancellationToken cancellationToken) =>
        ApproveCoreAsync(code, deviceName, screenGroupId, null, actor, cancellationToken);

    // Creates the group in the same save as the device, so a bad or expired code leaves no stray group behind.
    public Task<Guid> ApproveIntoNewGroupAsync(
        string code,
        string deviceName,
        string groupName,
        string actor,
        CancellationToken cancellationToken) =>
        ApproveCoreAsync(code, deviceName, null, groupName, actor, cancellationToken);

    private async Task<Guid> ApproveCoreAsync(
        string code,
        string deviceName,
        Guid? screenGroupId,
        string? newGroupName,
        string actor,
        CancellationToken cancellationToken)
    {
        var normalizedCode = new string(code.Where(char.IsAsciiDigit).ToArray());
        var codeHash = TokenUtility.Hash(normalizedCode);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.PairingSessions.SingleOrDefaultAsync(item => item.CodeHash == codeHash, cancellationToken)
            ?? throw new InvalidOperationException("The pairing code was not found.");
        if (!PairingRules.IsUsable(session, timeProvider.GetUtcNow()) || session.ApprovedDeviceId is not null)
        {
            throw new InvalidOperationException("The pairing code has expired or has already been used.");
        }
        if (screenGroupId is { } existingGroupId)
        {
            if (!await db.ScreenGroups.AnyAsync(item => item.Id == existingGroupId && !item.IsArchived, cancellationToken))
            {
                throw new InvalidOperationException("The selected screen group is unavailable.");
            }
        }
        else
        {
            var group = await CreateGroupAsync(db, newGroupName!, actor, cancellationToken);
            screenGroupId = group.Id;
        }

        var rawToken = TokenUtility.CreateToken(48);
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(deviceName) ? "New display" : deviceName.Trim(),
            ScreenGroupId = screenGroupId.Value,
            TokenHash = TokenUtility.Hash(rawToken),
            PairedUtc = timeProvider.GetUtcNow()
        };
        db.Devices.Add(device);
        session.ApprovedDeviceId = device.Id;
        session.ProtectedDeviceToken = protector.Protect(rawToken);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredUtc = timeProvider.GetUtcNow(),
            ActorType = "Human",
            ActorId = actor,
            Action = "DevicePaired",
            EntityType = nameof(Device),
            EntityId = device.Id.ToString(),
            Summary = $"Paired device {device.Name}."
        });
        await db.SaveChangesAsync(cancellationToken);
        return device.Id;
    }

    private async Task<ScreenGroup> CreateGroupAsync(SignageDbContext db, string name, string actor, CancellationToken cancellationToken)
    {
        name = name.Trim();
        var lowered = name.ToLower();
        // Names are unique, archived groups included.
        var existing = await db.ScreenGroups.AsNoTracking().FirstOrDefaultAsync(item => item.Name.ToLower() == lowered, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException(existing.IsArchived
                ? $"An archived screen group is already called \"{existing.Name}\". Choose a different name."
                : $"A screen group called \"{existing.Name}\" already exists. Choose it from the list instead.");
        }

        var group = new ScreenGroup { Id = Guid.NewGuid(), Name = name, CreatedUtc = timeProvider.GetUtcNow() };
        db.ScreenGroups.Add(group);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredUtc = timeProvider.GetUtcNow(),
            ActorType = "Human",
            ActorId = actor,
            Action = "ScreenGroupCreated",
            EntityType = nameof(ScreenGroup),
            EntityId = group.Id.ToString(),
            Summary = $"Created {group.Name}."
        });
        return group;
    }
}
