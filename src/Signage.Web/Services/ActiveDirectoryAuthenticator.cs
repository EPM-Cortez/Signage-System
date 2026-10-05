using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Signage.Application;

namespace Signage.Web.Services;

public sealed record DirectoryIdentity(string ExternalSubject, string LoginName, string DisplayName);

public interface IActiveDirectoryAuthenticator
{
    Task<DirectoryIdentity?> AuthenticateAsync(string username, string password, CancellationToken token);
}

public sealed class DirectoryUnavailableException() : Exception("The directory is unavailable. Try again later or contact IT.");

public static class DirectoryLoginNames
{
    public static string Normalize(string username, ActiveDirectoryOptions options)
    {
        var value = username.Trim();
        if (value.Length is < 1 or > 300 || value.Any(char.IsControl)) throw new ArgumentException("Enter your school username.");
        if (value.Contains('\\'))
        {
            var parts = value.Split('\\');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(options.NetBiosDomain)
                || !parts[0].Equals(options.NetBiosDomain, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(parts[1]) || parts[1].Contains('@'))
                throw new ArgumentException("Use your school username or full school sign-in name.");
            value = parts[1];
        }
        if (!value.Contains('@')) value += "@" + options.LoginSuffix;
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@')
            || !value[(at + 1)..].Equals(options.LoginSuffix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use your school username or full school sign-in name.");
        return value.ToLowerInvariant();
    }

    // RFC 4515 escaping applies even when a name has already been validated.
    public static string EscapeFilter(string value)
    {
        var result = new StringBuilder();
        foreach (var character in value)
            result.Append(character switch { '\\' => "\\5c", '*' => "\\2a", '(' => "\\28", ')' => "\\29", '\0' => "\\00", _ => character.ToString() });
        return result.ToString();
    }
}

public sealed class ActiveDirectoryAuthenticator(IOptions<ActiveDirectoryOptions> settings, ILogger<ActiveDirectoryAuthenticator> logger)
    : IActiveDirectoryAuthenticator, IDisposable
{
    // Bind is synchronous in the native LDAP library. Bound concurrency and timeouts
    // prevent a slow controller from exhausting the web server's worker threads.
    private readonly SemaphoreSlim connections = new(4, 4);

    public async Task<DirectoryIdentity?> AuthenticateAsync(string username, string password, CancellationToken token)
    {
        var options = settings.Value;
        if (!options.IsConfigured) throw new DirectoryUnavailableException();
        if (string.IsNullOrEmpty(password) || password.Length > 1024) return null; // Never permit an unauthenticated empty-password bind.
        var upn = DirectoryLoginNames.Normalize(username, options);
        if (!await connections.WaitAsync(TimeSpan.FromSeconds(2), token)) throw new DirectoryUnavailableException();
        try
        {
            return await Task.Run(() => BindAndRead(upn, password, options, token), token);
        }
        finally { connections.Release(); }
    }

    private DirectoryIdentity? BindAndRead(string upn, string password, ActiveDirectoryOptions options, CancellationToken token)
    {
        try
        {
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(options.Host, options.Port, true, false))
            {
                AuthType = AuthType.Basic,
                AutoBind = false,
                Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
            };
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.SessionOptions.SecureSocketLayer = true;
            // Use the platform's trust store and hostname checks. No certificate bypass
            // callback, plain LDAP fallback, service-account credential, or password log.
            token.ThrowIfCancellationRequested();
            connection.Bind(new NetworkCredential(upn, password));
            token.ThrowIfCancellationRequested();
            var request = new SearchRequest(options.BaseDn,
                $"(&(objectCategory=person)(objectClass=user)(userPrincipalName={DirectoryLoginNames.EscapeFilter(upn)}))",
                SearchScope.Subtree, "objectGUID", "userPrincipalName", "displayName", "userAccountControl")
            {
                SizeLimit = 2,
                TimeLimit = TimeSpan.FromSeconds(options.TimeoutSeconds)
            };
            var response = (SearchResponse)connection.SendRequest(request);
            token.ThrowIfCancellationRequested();
            if (response.Entries.Count != 1) return null;
            var entry = response.Entries[0];
            if (entry.Attributes["objectGUID"]?[0] is not byte[] { Length: 16 } guid) return null;
            var canonicalUpn = ReadText(entry, "userPrincipalName");
            if (!canonicalUpn.Equals(upn, StringComparison.OrdinalIgnoreCase)) return null;
            if (!int.TryParse(ReadText(entry, "userAccountControl"), CultureInfo.InvariantCulture, out var flags) || (flags & 2) != 0) return null;
            var displayName = ReadText(entry, "displayName");
            if (string.IsNullOrWhiteSpace(displayName)) displayName = canonicalUpn;
            return new DirectoryIdentity($"ad:{options.DomainName.ToLowerInvariant()}:{new Guid(guid):D}", canonicalUpn.ToLowerInvariant(), displayName[..Math.Min(200, displayName.Length)]);
        }
        catch (LdapException exception) when (exception.ErrorCode == 49) { return null; }
        catch (Exception exception) when (exception is LdapException or DirectoryOperationException or PlatformNotSupportedException or DllNotFoundException)
        {
            // Log an error category only: exception messages can include LDAP data.
            logger.LogWarning("Directory authentication unavailable ({ErrorCategory}).", exception.GetType().Name);
            throw new DirectoryUnavailableException();
        }
    }

    private static string ReadText(SearchResultEntry entry, string name) =>
        entry.Attributes[name]?.GetValues(typeof(string)).FirstOrDefault() as string ?? string.Empty;

    public void Dispose() => connections.Dispose();
}
