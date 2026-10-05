#!/usr/bin/env bash
set -euo pipefail
backup="${1:?usage: restore.sh BACKUP_DIRECTORY DATA_ROOT [SERVICE_NAME|-] [CONFIG_DESTINATION|-] [HEALTH_URL]}"
data_root="${2:?usage: restore.sh BACKUP_DIRECTORY DATA_ROOT [SERVICE_NAME|-] [CONFIG_DESTINATION|-] [HEALTH_URL]}"
service_name="${3:--}"
config_destination="${4:--}"
health_url="${5:-http://127.0.0.1:5080/health/ready}"
[[ -f "$backup/inventory.sha256" && -f "$backup/data/signage.db" ]] || { echo 'Backup or inventory is incomplete.' >&2; exit 1; }
(cd "$backup" && sha256sum --check --strict inventory.sha256)
if [[ "$service_name" != "-" ]] && systemctl is-active --quiet "$service_name"; then echo "Service $service_name must be stopped before restore." >&2; exit 1; fi
if command -v flock >/dev/null && [[ -e "$data_root/signage.lock" ]] && ! flock -n "$data_root/signage.lock" -c true; then echo 'The data root is in use.' >&2; exit 1; fi
pre_restore="$data_root.pre-restore-$(date -u +%Y%m%d-%H%M%SZ)"
if [[ -e "$data_root" ]]; then mv -- "$data_root" "$pre_restore"; fi
mkdir -p "$data_root"
cp -a "$backup/data/." "$data_root/"
if [[ "$config_destination" != "-" && -f "$backup/config/appsettings.Production.json" ]]; then cp "$backup/config/appsettings.Production.json" "$config_destination"; fi
echo "Restore complete. Pre-restore data: $pre_restore"
if [[ "$service_name" != "-" ]]; then sudo systemctl start "$service_name"; curl --fail --silent --show-error --max-time 30 "$health_url" >/dev/null; fi
