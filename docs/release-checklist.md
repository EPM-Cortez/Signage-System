# Release, upgrade, and rollback checklist

## Before release

- [ ] Stable .NET 10 and Node.js 22 are installed; no preview SDK is selected.
- [ ] `setup-local` passes all .NET, converter, player, and Playwright tests.
- [ ] Dependency vulnerability/licence review is complete.
- [ ] Private renderer corpus is compared with PowerPoint screenshots.
- [ ] OIDC teacher/admin claims are validated in staging.
- [ ] TLS, allowed hosts, proxy forwarding, upload limits, paths, disk headroom, and service account ACLs are reviewed.
- [ ] Pair, download, restart, offline playback, corrupt-package rejection, and revoke behavior are tested on kiosk hardware.
- [ ] A backup and separate restore drill succeeds.

## Upgrade

- [ ] Publish a versioned release and retain its dependency lockfiles.
- [ ] Take and verify a backup.
- [ ] Stop the service and verify the data-root lock is free.
- [ ] Preserve current binaries/config as `previous`.
- [ ] Run `Signage.Web.dll --migrate` before starting the service.
- [ ] Verify live/ready health, sign-in, queued jobs, current publications, and a player heartbeat.
- [ ] Keep the previous release and pre-upgrade backup until the observation window ends.

## Deliberate trade-offs

- SQLite/local files keep installation and recovery simple, at the cost of a strict single-host limit.
- The worker lives in the web host, avoiding a separate service while limiting conversion concurrency to a small configured value.
- PNG packages prioritise deterministic offline playback over animation and editability.
- Browser players pre-download everything, increasing initial bandwidth/storage but making activation atomic and offline-safe.
- Server-rendered Razor Pages keep the management UI small and dependency-light; the player remains a focused TypeScript application.
- Device hashes avoid recoverable server credentials, but re-pairing is required after local credential loss.
- Automatic migrations are Development-only; production upgrades are more explicit and safer but require an operator step.
- Retention archives old content metadata and deletes unprotected files instead of deleting audit/history rows.
