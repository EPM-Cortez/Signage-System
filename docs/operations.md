# Operations guide

## Production layout

Publish with `publish-release.ps1` on Windows or `publish-release.sh` on Linux. Native converter dependencies are platform-specific; the GitHub ZIP is Windows x64 only. See the [production quickstart](production-quickstart.md) for runtime requirements. Copy `app/appsettings.Production.sample.json` to `app/appsettings.Production.json`, set site-specific paths/hostnames, and configure [AD LDAPS](active-directory.md) or OIDC. For OIDC, provide `Authentication__ClientSecret` through the service environment or secret store. Do not put secrets in committed JSON.

The release supports:

- Windows Service through `deploy/install-windows-service.ps1`.
- systemd through `deploy/school-signage.service`.
- Nginx TLS proxy through `deploy/nginx-school-signage.conf`.

Run the migration command from the release `app` directory while the service is stopped, then start it and verify `/health/live` and `/health/ready`. Readiness may be degraded when the converter is unavailable while cached signage continues to play. `/metrics` is an administrator-authenticated Prometheus text endpoint.

Only one process may use a data root. Do not use a network share for the SQLite database or content directory. Keep the database, sources, packages, temporary directory, data-protection keys, and backup target on storage with enough free space. The admin System page displays resolved non-secret paths.

## Backup and restore

Backup scripts stop the named service when supplied, obtain the data-root lock, copy the database/content/configuration, and write a SHA-256 inventory. Current players continue from their caches during the brief stop.

```powershell
.\scripts\backup.ps1 -DataRoot 'C:\SchoolSignage\current\data' -BackupRoot 'D:\SchoolSignageBackups' -ServiceName SchoolSignage -ConfigPath 'C:\SchoolSignage\current\app\appsettings.Production.json'
```

```bash
./scripts/backup.sh /opt/school-signage/current/data /srv/signage-backups school-signage /opt/school-signage/current/app/appsettings.Production.json
```

Restore validates every inventory hash, refuses a running data root, moves the existing data to a timestamped `pre-restore` directory, restores to a clean root, and can restart/health-check the service.

```powershell
.\scripts\restore.ps1 -BackupDirectory 'D:\SchoolSignageBackups\20260720-230000Z' -DataRoot 'C:\SchoolSignage\current\data' -ServiceName SchoolSignage
```

Always test restore on a separate host or directory before relying on a backup schedule.

## Maintenance

The scheduled job removes expired pairing sessions, abandoned `.uploading` files, stale conversion attempt directories, and versions older than the configured retention count. Active/future publications and current presentation versions are protected. Set `Storage:CleanupDryRun` to `true` to log candidates without mutation. `PRAGMA optimize` runs after a live cleanup pass.

Run `VACUUM` manually only during a planned service stop after a large deletion and after taking a verified backup. It requires additional free disk space and should not run during publication activity.

## Upgrade and rollback

The `scripts/upgrade` examples stop the service, preserve `current` as `previous`, retain data/configuration, run `--migrate`, and restore the previous release if migration fails. Take a verified backup first. Database migrations are forward operations; a code rollback after a successful schema migration must use a release whose data compatibility is documented.

## Monitoring

Alert on readiness degradation, low disk, queued/failed conversions, offline displays, and repeated assignment errors. Preserve structured JSON logs in production. Correlate requests using the `X-Correlation-ID` response header. Review audit events for publish, pairing, revoke, retry, and rollback actions.
