#!/usr/bin/env bash
set -Eeuo pipefail
stage=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
[[ $(systemctl is-active school-signage) == active ]]
[[ $(systemctl is-enabled school-signage) == enabled ]]
response_file=$(mktemp)
trap 'rm -f "$response_file"' EXIT
for probe in '/health/live:200' '/health/ready:200' '/dev-login:404' '/api/screen-groups/available:401' '/player/:200' '/Login:200' '/Admin:302'; do
    route=${probe%:*}; expected=${probe##*:}
    actual=$(curl -sS --max-time 15 --cacert "$stage/server.crt" -o "$response_file" -w '%{http_code}' "https://10.154.100.200$route")
    [[ $actual == "$expected" ]] || { echo "$route returned $actual, expected $expected" >&2; exit 1; }
    if [[ $route == /Login ]]; then grep -q 'type="password"' "$response_file"; ! grep -q 'Development sign-in' "$response_file"; fi
    printf 'HTTPS probe passed: %s (%s)\n' "$route" "$actual"
done
[[ $(curl -sS -o /dev/null -w '%{http_code}' http://10.154.100.200/Login) == 301 ]]
echo 'Production service, HTTPS, directory login form, guarded APIs and player passed.'
