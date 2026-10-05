#!/usr/bin/env bash
set -euo pipefail
incoming="${1:?usage: upgrade.sh RELEASE_ROOT INSTALL_ROOT [SERVICE_NAME]}"
install="${2:?usage: upgrade.sh RELEASE_ROOT INSTALL_ROOT [SERVICE_NAME]}"
service_name="${3:-school-signage}"
[[ -f "$incoming/app/Signage.Web.dll" ]] || { echo 'Incoming release is invalid.' >&2; exit 1; }
current="$install/current"; previous="$install/previous"
sudo systemctl stop "$service_name"
rm -rf -- "$previous"
[[ ! -e "$current" ]] || mv -- "$current" "$previous"
cp -a -- "$incoming" "$current"
[[ ! -d "$previous/data" ]] || { rm -rf -- "$current/data"; cp -a "$previous/data" "$current/data"; }
[[ ! -f "$previous/app/appsettings.Production.json" ]] || cp "$previous/app/appsettings.Production.json" "$current/app/appsettings.Production.json"
if ! (cd "$current/app" && ASPNETCORE_ENVIRONMENT=Production dotnet Signage.Web.dll --migrate); then
  rm -rf -- "$current"; mv -- "$previous" "$current"; echo 'Upgrade rolled back after migration failure.' >&2; exit 1
fi
sudo systemctl start "$service_name"
curl --fail --silent --show-error --max-time 30 http://127.0.0.1:5080/health/ready >/dev/null
