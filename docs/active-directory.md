# Active Directory sign-in and staff permissions

This app supports an on-premises Active Directory domain through **LDAPS**. It checks a staff member's username/password with the domain controller, reads their stable AD `objectGUID`, and creates a secure local session. It never stores the password and does not need a directory service-account password. Directory sign-in does not modify AD users or groups.

An Entra-only tenant should use `Authentication:Mode=OpenIdConnect`; it cannot be reached through this LDAPS integration. OIDC remains supported. At first OIDC sign-in, the configured teacher/admin role claims establish an initial local role; subsequent permission changes are controlled by this app, not reapplied from the token.

## Configure the server

Back up the SQLite database and content before upgrading. Apply migrations with the existing `--migrate` operation before starting Production (Development auto-migrates). The StaffAccounts migration is additive: existing presentations, devices, publisher grants, and videos are retained. Existing cookies without the new account/provider claims require one fresh sign-in.

Use service environment variables or a private server configuration file. These example values are placeholders, not a detected school domain:

```json
{
  "Authentication": {
    "Mode": "ActiveDirectory",
    "SessionMinutes": 480
  },
  "ActiveDirectory": {
    "Host": "dc01.school.example",
    "Port": 636,
    "DomainName": "school.example",
    "UpnSuffix": "school.example",
    "NetBiosDomain": "SCHOOL",
    "BaseDn": "DC=school,DC=example",
    "BootstrapAdminUpn": "signage.admin@school.example",
    "TimeoutSeconds": 10
  }
}
```

`DomainName` identifies this AD domain. `UpnSuffix` is optional and defaults to DomainName; use it when staff UPNs have a different suffix. Sign-in accepts a short username, a full UPN using that suffix, or `DOMAIN\username` matching the configured NetBiosDomain. Short usernames must correspond to the UPN prefix; if a school's sAMAccountName and UPN prefix differ, use the full UPN. BaseDn restricts searches to the staff domain/OU. Users must be able to read their own GUID, UPN, display name, and userAccountControl.

Use the controller's DNS hostname matching its LDAPS certificate. The certificate chain must be trusted by the signage service's OS account. The client has no plaintext LDAP fallback, certificate bypass, or referral chasing. Windows uses the platform LDAP client; Linux also requires its native LDAP library/trust configuration (for example libldap on Ubuntu). Verify the target host rather than assuming a development-machine test covers deployment.

Expose the staff site through HTTPS, such as the supplied reverse-proxy setup, with an appropriate certificate and trusted proxy forwarding configuration. Cookie credentials are Secure/HttpOnly. Real password forms are disabled over HTTP, including localhost:5179. Do not turn off that check to test real credentials. See Microsoft's [LDAPS certificate requirements](https://learn.microsoft.com/en-us/troubleshoot/windows-server/active-directory/enable-ldap-over-ssl-3rd-certification-authority).

## First administrator and testing

1. Set BootstrapAdminUpn to the exact initial administrator UPN **before their first AD sign-in**. Only that successfully verified AD account can bootstrap the first directory administrator. New staff otherwise start Pending.
2. Restart with the directory configuration, then open the HTTPS staff URL and sign in. Development demo links are unavailable in ActiveDirectory mode and are never permitted in Production.
3. Under **Active Directory → Test a staff sign-in**, test an account without changing the current session or assigning permissions. Successful output confirms the bind and identity lookup; passwords are cleared from redisplayed forms. Failed attempts count toward the normal AD account-lockout policy.
4. After confirming an enabled AD administrator, remove BootstrapAdminUpn from deployment configuration and use Staff access for future role changes.

You can configure/test the directory while still using Development authentication, but the test page still requires HTTPS. It is read-only with respect to staff permissions. Do not share a Development-authentication URL with staff.

## Staff access

- **Administrator:** manage staff, devices, groups, schedules, and all presentations; publish to all active screen groups.
- **Teacher:** upload to their assigned active screen groups and view their own presentations. Selecting another group or posting its ID directly is rejected server-side.
- **Pending:** sign in and request approval, but cannot upload, inspect staff content, or use administrator APIs.
- **Disabled:** no new sign-in; an existing local session is rejected on its next request.

An administrator can pre-add staff by school username, choose their role and enabled state, and check allowed screen groups. Their verified AD GUID is linked on first sign-in. Alternatively staff can sign in first and then be approved from the pending account list. No password is entered into the staff-management form.

Roles are read from SQLite on every authenticated request. Group grants are queried when loading the picker and checked again before saving an upload. The conversion worker rechecks grants before auto-publishing: if access is removed while rendering, affected targets are skipped and recorded in the audit log. Previously published content is **not** automatically removed by a role change; administrators can disable publications from Schedule. At least one enabled administrator who has already verified their identity must remain for each sign-in method.

After a successful first bind, permissions are tied to the GUID rather than a mutable username. A deleted/recreated AD account cannot inherit the former account's grants simply by reusing its username. Directory rename collisions need explicit administrator resolution rather than automatic privilege transfer.

## Session and operational limits

Sessions are non-persistent browser cookies with an eight-hour default expiry and no sliding refresh. Local role/name changes may renew the cookie. AD password/disabled-account status is checked at sign-in, not on every page request: existing sessions are not continuously bound to AD. Disable the local signage account for immediate app revocation, or shorten SessionMinutes to meet school policy. This direct password flow does not supply MFA; use the existing OIDC integration when MFA/conditional-access is required.

Credential routes are limited to 20 requests/minute per source IP (including page loads) and four concurrent native LDAP operations, with configurable 2–30-second LDAP timeouts. Do not run password spraying or repeated diagnostics against real staff accounts. Protect the management URL with school-network/VPN access and upstream abuse controls.

Automated tests use a fake directory with isolated SQLite databases to verify permissions, CSRF, HTTPS enforcement, stable identities, and cookie revocation. They do not prove a real controller's certificate, bind policy, network reachability, Linux native-library setup, or school browser configuration. Run the HTTPS test page against real AD before sharing the URL.
