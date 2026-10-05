using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Signage.Application;
using Signage.Domain;
using Signage.Infrastructure.Persistence;
using AuthenticationOptions = Signage.Application.AuthenticationOptions;

namespace Signage.Web.Services;

public sealed class StaffAccessService(
    IDbContextFactory<SignageDbContext> factory,
    IOptions<ActiveDirectoryOptions> directoryOptions,
    IOptions<AuthenticationOptions> authenticationOptions,
    TimeProvider clock)
{
    public async Task<StaffAccount?> FindAsync(string subject, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.StaffAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.IdentitySubject == subject, token);
    }

    public async Task<StaffAccount?> SignInDirectoryAsync(DirectoryIdentity identity, CancellationToken token)
    {
        var login = DirectoryLoginNames.Normalize(identity.LoginName, directoryOptions.Value);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var account = await db.StaffAccounts.SingleOrDefaultAsync(item => item.Provider == "ActiveDirectory" && item.ExternalSubject == identity.ExternalSubject, token);
        var byName = await db.StaffAccounts.SingleOrDefaultAsync(item => item.Provider == "ActiveDirectory" && item.NormalizedLoginName == login, token);
        // A recycled username cannot inherit the old AD object's permissions.
        if (account is not null && byName is not null && byName.Id != account.Id) return null;
        if (account is null && byName?.ExternalSubject is not null && byName.ExternalSubject != identity.ExternalSubject) return null;
        account ??= byName;
        if (account is null)
        {
            var bootstrap = directoryOptions.Value.BootstrapAdminUpn.Trim().ToLowerInvariant();
            var firstAdmin = login == bootstrap && !await db.StaffAccounts.AnyAsync(item => item.Provider == "ActiveDirectory" && item.Role == StaffRole.Administrator && item.ExternalSubject != null, token);
            account = NewAccount("ActiveDirectory", login, identity.DisplayName, firstAdmin ? StaffRole.Administrator : StaffRole.Pending);
            db.StaffAccounts.Add(account);
            db.AuditEvents.Add(Audit("System", "directory-sign-in", firstAdmin ? "InitialAdministratorCreated" : "StaffApprovalRequested", account.Id, firstAdmin ? "Created the configured initial AD administrator." : "A verified directory account is awaiting approval."));
        }
        if (!account.IsEnabled) return null;
        account.ExternalSubject = identity.ExternalSubject;
        account.LoginName = identity.LoginName;
        account.NormalizedLoginName = login;
        account.DisplayName = identity.DisplayName[..Math.Min(200, identity.DisplayName.Length)];
        account.LastSignInUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return account;
    }

    public async Task<StaffAccount> SignInFederatedAsync(ClaimsPrincipal principal, CancellationToken token)
    {
        var subject = principal.FindFirstValue("sub") ?? throw new InvalidOperationException("The identity provider did not supply a subject.");
        var issuer = principal.FindFirstValue("iss") ?? authenticationOptions.Value.Authority ?? string.Empty;
        var external = issuer + "|" + subject;
        if (subject.Length > 300 || external.Length > 500) throw new InvalidOperationException("The identity provider subject is too long.");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var account = await db.StaffAccounts.SingleOrDefaultAsync(item => item.Provider == "OpenIdConnect" && item.ExternalSubject == external, token);
        if (account is null)
        {
            var options = authenticationOptions.Value;
            bool HasRole(string role) => principal.Claims.Any(claim => (claim.Type is "role" or "roles" || claim.Type == ClaimTypes.Role) && claim.Value == role);
            account = NewAccount("OpenIdConnect", subject, principal.Identity?.Name ?? subject,
                HasRole(options.AdminRoleClaim) ? StaffRole.Administrator : HasRole(options.TeacherRoleClaim) ? StaffRole.Teacher : StaffRole.Pending);
            // Retain ownership and publisher grants from the existing OIDC implementation.
            account.IdentitySubject = subject;
            account.ExternalSubject = external;
            db.StaffAccounts.Add(account);
        }
        account.LastSignInUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return account;
    }

    public async Task<Guid> AddDirectoryStaffAsync(string username, StaffRole role, bool enabled, IReadOnlyCollection<Guid> groupIds, string actor, CancellationToken token)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentException("Choose a valid role.");
        if (!directoryOptions.Value.IsConfigured) throw new ArgumentException("Configure Active Directory before adding directory staff.");
        var login = DirectoryLoginNames.Normalize(username, directoryOptions.Value);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (await db.StaffAccounts.AnyAsync(item => item.Provider == "ActiveDirectory" && item.NormalizedLoginName == login, token))
            throw new ArgumentException("This staff account already exists. Edit its permissions below.");
        await ValidateGroupsAsync(db, groupIds, token);
        var account = NewAccount("ActiveDirectory", login, login, role);
        account.IsEnabled = enabled;
        db.StaffAccounts.Add(account);
        foreach (var id in groupIds.Distinct()) db.PublisherAccess.Add(new PublisherAccess { IdentitySubject = account.IdentitySubject, ScreenGroupId = id });
        db.AuditEvents.Add(Audit("Human", actor, "StaffAccountAdded", account.Id, $"Added {login} as {role}; enabled={enabled}."));
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return account.Id;
    }

    public async Task UpdateAsync(Guid id, StaffRole role, bool enabled, IReadOnlyCollection<Guid> groupIds, string actor, CancellationToken token)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentException("Choose a valid role.");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var account = await db.StaffAccounts.SingleOrDefaultAsync(item => item.Id == id, token)
            ?? throw new ArgumentException("Staff account not found.");
        if (account.IsEnabled && account.Role == StaffRole.Administrator && (!enabled || role != StaffRole.Administrator)
            && !await db.StaffAccounts.AnyAsync(item => item.Id != id && item.Provider == account.Provider && item.IsEnabled && item.Role == StaffRole.Administrator && item.ExternalSubject != null, token))
            throw new ArgumentException("Keep at least one enabled administrator for this sign-in method.");
        await ValidateGroupsAsync(db, groupIds, token);
        var oldRole = account.Role;
        account.Role = role;
        account.IsEnabled = enabled;
        var oldGrants = await db.PublisherAccess.Where(item => item.IdentitySubject == account.IdentitySubject).ToListAsync(token);
        var requested = groupIds.Distinct().ToHashSet();
        db.PublisherAccess.RemoveRange(oldGrants.Where(item => !requested.Contains(item.ScreenGroupId)));
        foreach (var group in requested.Where(group => oldGrants.All(item => item.ScreenGroupId != group)))
            db.PublisherAccess.Add(new PublisherAccess { IdentitySubject = account.IdentitySubject, ScreenGroupId = group });
        db.AuditEvents.Add(Audit("Human", actor, "StaffPermissionsChanged", account.Id, $"Updated {account.LoginName}: {oldRole} → {role}; enabled={enabled}; {requested.Count} group grant(s)."));
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    public static ClaimsPrincipal CreatePrincipal(StaffAccount account)
    {
        var claims = new List<Claim>
        {
            new("sub", account.IdentitySubject), new(ClaimTypes.NameIdentifier, account.IdentitySubject),
            new(ClaimTypes.Name, account.DisplayName), new("signage:provider", account.Provider)
        };
        if (account.Role == StaffRole.Administrator) claims.Add(new(ClaimTypes.Role, "SignageAdmin"));
        else if (account.Role == StaffRole.Teacher) claims.Add(new(ClaimTypes.Role, "Teacher"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private StaffAccount NewAccount(string provider, string login, string displayName, StaffRole role)
    {
        var id = Guid.NewGuid();
        return new StaffAccount { Id = id, IdentitySubject = $"staff:{id:D}", Provider = provider, LoginName = login,
            NormalizedLoginName = login.ToLowerInvariant(), DisplayName = displayName[..Math.Min(200, displayName.Length)], Role = role, CreatedUtc = clock.GetUtcNow() };
    }

    private static async Task ValidateGroupsAsync(SignageDbContext db, IReadOnlyCollection<Guid> groupIds, CancellationToken token)
    {
        var distinct = groupIds.Distinct().ToArray();
        if (await db.ScreenGroups.CountAsync(item => distinct.Contains(item.Id) && !item.IsArchived, token) != distinct.Length)
            throw new ArgumentException("Choose active screen groups only.");
    }

    private AuditEvent Audit(string actorType, string actor, string action, Guid id, string summary) => new()
    {
        Id = Guid.NewGuid(), OccurredUtc = clock.GetUtcNow(), ActorType = actorType, ActorId = actor,
        Action = action, EntityType = nameof(StaffAccount), EntityId = id.ToString(), Summary = summary[..Math.Min(500, summary.Length)]
    };
}
