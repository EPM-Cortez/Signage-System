#!/usr/bin/env bash
# Apply a prevalidated release, preserving existing configuration and data.
set -Eeuo pipefail
umask 022
[[ $(id -u) == 0 && $(hostname -s) == localllm ]] || { echo 'Run with sudo on localllm.' >&2; exit 1; }
stage=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
release_id=${1:?Release ID required}
archive_hash=${2:?Archive SHA256 required}
expected_current=${3:?Expected current release required}
[[ $release_id =~ ^linux-ui-[0-9]{8}-[0-9]{6}$ && $archive_hash =~ ^[a-f0-9]{64}$ ]]
[[ $expected_current =~ ^/opt/school-signage/releases/[A-Za-z0-9-]+$ ]]
install_root=/opt/school-signage
release="$install_root/releases/$release_id"
backup="/var/backups/school-signage/pre-$release_id"
failed_data="/var/lib/school-signage.failed-$release_id"
[[ -L "$install_root/current" && $(readlink -f "$install_root/current") == "$expected_current" ]] || { echo 'The current release changed; refusing a stale upgrade.' >&2; exit 1; }
[[ ! -e "$release" && ! -e "$backup" && ! -e "$failed_data" ]] || { echo 'An upgrade with this ID already exists.' >&2; exit 1; }
[[ $(cat "$stage/smoke/PASSED") == "$archive_hash" ]] || { echo 'Linux validation has not passed.' >&2; exit 1; }
[[ -f /etc/school-signage/appsettings.Production.json && -d /var/lib/school-signage && -f "$stage/server.crt" ]]
printf '%s  %s\n' "$archive_hash" "$stage/$release_id.tar.gz" | sha256sum -c -
(cd "$stage/package" && sha256sum --quiet -c SHA256SUMS)
[[ $(systemctl is-active school-signage) == active ]]
touch "$stage/upgrade.log"
chown root:ai "$stage/upgrade.log"
chmod 0640 "$stage/upgrade.log"
exec > >(tee -a "$stage/upgrade.log") 2>&1
service_stopped=false
activated=false
backup_ready=false
rollback() {
    local status=$?
    trap - ERR INT TERM
    set +e
    [[ $status -ne 0 ]] || status=1
    echo "Upgrade failed at line ${BASH_LINENO[0]}; restoring the previous release." >&2
    if [[ $service_stopped == true ]]; then
        systemctl stop school-signage
        if [[ $activated == true ]]; then
            ln -s "$expected_current" "$install_root/current.rollback-$release_id"
            mv -Tf "$install_root/current.rollback-$release_id" "$install_root/current"
            if [[ $backup_ready == true ]]; then
                mv /var/lib/school-signage "$failed_data"
                cp -a "$backup/data" /var/lib/school-signage
            fi
        fi
        systemctl start school-signage
        systemctl is-active school-signage
    fi
    printf 'Upgrade failed at %s; inspect upgrade.log.\n' "$(date -Is)" >"$stage/FAILED"
    chmod 0644 "$stage/FAILED"
    exit "${status:-1}"
}
trap rollback ERR INT TERM
install -d -m 0755 "$install_root" "$install_root/releases" "$release"
cp -a "$stage/package/." "$release/"
chown -R root:root "$release"
find "$release" -type d -exec chmod 0755 {} +
find "$release" -type f -exec chmod 0644 {} +
[[ ! -e "$release/app/appsettings.Production.json" ]]
ln -s /etc/school-signage/appsettings.Production.json "$release/app/appsettings.Production.json"
runuser -u signage -- test -r "$release/app/Signage.Web.dll"
runuser -u signage -- test -r "$release/converter/dist/cli.js"
nginx -t
echo 'Stopping Signage briefly to take a consistent backup.'
systemctl stop school-signage
service_stopped=true
install -d -m 0700 "$backup"
cp -a /var/lib/school-signage "$backup/data"
cp -a /etc/school-signage "$backup/configuration"
printf '%s\n' "$expected_current" >"$backup/previous-release.txt"
backup_ready=true
echo "Backup saved to $backup"
activated=true
ln -s "$release" "$install_root/current.new-$release_id"
mv -Tf "$install_root/current.new-$release_id" "$install_root/current"
runuser -u signage -- bash -c 'cd /opt/school-signage/current/app && ASPNETCORE_ENVIRONMENT=Production /usr/bin/dotnet Signage.Web.dll --migrate'
python3 - "$backup/data/signage.db" /var/lib/school-signage/signage.db <<'PY'
import sqlite3, sys
before, after = [sqlite3.connect(f'file:{path}?mode=ro', uri=True) for path in sys.argv[1:]]
assert after.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
assert after.execute('PRAGMA foreign_key_check').fetchall() == []
for (table,) in before.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name != '__EFMigrationsHistory'"):
    safe = table.replace('"', '""')
    query = f'SELECT COUNT(*) FROM "{safe}"'
    assert before.execute(query).fetchone() == after.execute(query).fetchone(), f'Row count changed in {table}'
assert after.execute("SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId='20261006144313_LibraryManagement'").fetchone()
print('Database migration passed: integrity, foreign keys and existing record counts preserved')
PY
systemctl start school-signage
ready=false
for attempt in $(seq 1 45); do
    if [[ $(curl -fsS --max-time 3 --cacert "$stage/server.crt" https://10.154.100.200/health/ready 2>/dev/null || true) == Healthy ]]; then ready=true; break; fi
    sleep 1
done
[[ $ready == true ]] || { journalctl -u school-signage -n 60 --no-pager; echo 'Updated service did not become ready.' >&2; false; }
bash "$stage/verify-internal-linux.sh"
for asset in js/prefs.js css/site.css js/site.js js/publish.js js/status.js; do
    curl -fsS --max-time 15 --cacert "$stage/server.crt" "https://10.154.100.200/$asset" -o "$stage/smoke/live-asset"
    cmp "$release/app/wwwroot/$asset" "$stage/smoke/live-asset"
done
ln -s "$expected_current" "$install_root/previous.new-$release_id"
[[ ! -e "$install_root/previous" || -L "$install_root/previous" ]]
mv -Tf "$install_root/previous.new-$release_id" "$install_root/previous"
printf '%s\n' "$release_id" >"$stage/INSTALLED"
chmod 0644 "$stage/INSTALLED"
trap - ERR INT TERM
printf '\nUpdated Signage is running at https://10.154.100.200/Login\nRelease: %s\nBackup: %s\n' "$release_id" "$backup"
