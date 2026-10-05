# Windows production build

The GitHub release ZIP is a **Windows x64, framework-dependent** build. It includes the published web app/player, SQLite native libraries, production converter dependencies (including native Windows modules), bundled fonts/licences, deployment scripts, and documentation. It does not include school PowerPoints, databases, accounts, secrets, or development sign-in configuration. This is a build package, not an already-configured school deployment.

## Prerequisites

- Windows x64 with the **ASP.NET Core Runtime 10.0.10 or a newer 10.0 patch** (not just the .NET desktop runtime).
- **Node.js 22.23.1 or a newer 22.x patch**, accessible to the service account. Alternatively configure `Rendering:NodeExecutable` with its absolute executable path.
- A trusted HTTPS endpoint for the staff portal and player service worker, plus a dedicated service account with appropriate local data/configuration permissions.
- An AD domain controller with trusted LDAPS on port 636, or an OIDC identity provider. Real AD integration must be tested on the school network.

Do not use this Windows ZIP on Linux or ARM64. Build the converter dependencies on the target platform using the source scripts instead.

## Install and configure

1. Verify the ZIP against the accompanying `.sha256` file, then extract it to a local folder such as `C:\SchoolSignage\current`. Keep data and backups on local storage, not a network share.
2. Copy `app\appsettings.Production.sample.json` to `app\appsettings.Production.json`. The sample supplies the correct relative paths for the release layout. Set `AllowedHosts` to the real staff hostname.
3. For AD, set `Authentication:Mode` to `ActiveDirectory`, then set `ActiveDirectory:Host`, `DomainName`, `UpnSuffix`, `NetBiosDomain`, `BaseDn`, and `BootstrapAdminUpn`. The bootstrap UPN must identify the intended initial administrator. See [the AD guide](active-directory.md) for an example and certificate requirements. No staff password is stored in this file. For OIDC, retain `OpenIdConnect`, configure the provider, and supply its client secret through the service environment/secret store.
4. From the `app` directory, apply migrations and check the app interactively:

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = 'Production'
   dotnet .\Signage.Web.dll --migrate
   dotnet .\Signage.Web.dll --urls http://127.0.0.1:5080
   ```

5. Put the loopback listener behind a trusted local HTTPS reverse proxy. Do not expose port 5080 directly to staff. Verify `/health/live` and `/health/ready`, then stop the interactive process and install a managed service using `deploy\install-windows-service.ps1` from an elevated shell. Use a least-privileged service account and ensure Node is accessible to it; do not leave a production service running as LocalSystem.
6. Sign in over HTTPS with the configured bootstrap AD administrator. Test LDAPS from **Admin → Active Directory**, then approve staff in **Admin → Staff access**, assign Teacher/Administrator roles, and select each teacher's permitted screen groups. New unapproved accounts have no publishing access.
7. Create screen groups and pair displays at `/player/`. Validate representative school PowerPoints, embedded video playback, kiosk/offline behavior, and backup/restore before rollout. Embedded MP4/WebM is muted and plays to completion; YouTube embeds require internet access and may be unavailable due to provider restrictions.

See [operations](operations.md), [kiosk setup](kiosk-player.md), [rendering limitations](renderer-compatibility.md), and [the release checklist](release-checklist.md). HTTPS/AD/network/hardware checks cannot be proved by the automated GitHub build.
