#!/usr/bin/env bash
set -euo pipefail
data_root="${1:?usage: backup.sh DATA_ROOT BACKUP_ROOT [SERVICE_NAME|-] [CONFIG_PATH|-] [HEALTH_URL]}"
backup_root="${2:?usage: backup.sh DATA_ROOT BACKUP_ROOT [SERVICE_NAME|-] [CONFIG_PATH|-] [HEALTH_URL]}"
service_name="${3:--}"
config_path="${4:--}"
health_url="${5:-http://127.0.0.1:5080/health/ready}"
[[ -f "$data_root/signage.db" ]] || { echo "signage.db not found under $data_root" >&2; exit 1; }
was_running=false
if [[ "$service_name" != "-" ]] && systemctl is-active --quiet "$service_name"; then
  was_running=true; sudo systemctl stop "$service_name"
fi
restart_service() {
  if $was_running; then sudo systemctl start "$service_name"; curl --fail --silent --show-error --max-time 30 "$health_url" >/dev/null || true; fi
}
trap restart_service EXIT
if command -v flock >/dev/null && ! flock -n "$data_root/signage.lock" -c true; then echo 'The data root is still in use.' >&2; exit 1; fi
destination="$backup_root/$(date -u +%Y%m%d-%H%M%SZ)"
mkdir -p "$destination/data"
cp -a "$data_root/." "$destination/data/"
if [[ "$config_path" != "-" && -f "$config_path" ]]; then mkdir -p "$destination/config"; cp "$config_path" "$destination/config/appsettings.Production.json"; fi
(cd "$destination" && find . -type f ! -name inventory.sha256 -print0 | sort -z | xargs -0 sha256sum > inventory.sha256)
echo "Backup complete: $destination"
