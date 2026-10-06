#!/usr/bin/env bash
# Validate a new release using separate test data before touching the service.
set -Eeuo pipefail
umask 022
[[ $(id -un) == ai && $(hostname -s) == localllm ]] || { echo 'Unexpected deployment user or host.' >&2; exit 1; }
stage=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
release_id=${1:?Release ID required}
archive_hash=${2:?Archive SHA256 required}
[[ $release_id =~ ^linux-ui-[0-9]{8}-[0-9]{6}$ && $archive_hash =~ ^[a-f0-9]{64}$ ]]
cd "$stage"
printf '%s  %s\n' "$archive_hash" "$release_id.tar.gz" | sha256sum -c -
[[ ! -e package && ! -e smoke ]] || { echo 'Use a fresh staging directory.' >&2; exit 1; }
mkdir package smoke
tar -xzf "$release_id.tar.gz" --no-same-owner -C package
(cd package && sha256sum --quiet -c SHA256SUMS)
python3 - "$stage/package/release.json" "$release_id" <<'PY'
import json, pathlib, sys
metadata = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
assert metadata['releaseId'] == sys.argv[2] and metadata['platform'] == 'linux-x64'
PY
export PATH="/opt/school-signage/runtime/node/bin:$PATH"
export DOTNET_CLI_HOME="$stage/smoke/dotnet-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
export LDAPTLS_CACERT=/usr/local/share/ca-certificates/str-enterprise-root-ca-01.crt LDAPTLS_REQCERT=demand
(cd package/converter && npm ci --omit=dev --no-audit --no-fund)
node package/converter/dist/cli.js health
python3 - "$stage" <<'PY'
import json, pathlib, sys
stage = pathlib.Path(sys.argv[1])
settings = {'outputWidth':1920,'fontDirectories':[str(stage/'package/fonts')],'useSystemFonts':False,'fontMapping':{'Arial':'Arimo','Calibri':'Carlito'},'fitTextToBox':True,'requestedSourceSlideNumbers':[1],'timeoutSeconds':60,'diagnosticVerbosity':'normal'}
(stage/'smoke/render-settings.json').write_text(json.dumps(settings))
PY
node package/converter/dist/cli.js render --input "$stage/playwright-welcome.pptx" --output "$stage/smoke/render" --settings "$stage/smoke/render-settings.json"
python3 - "$stage" <<'PY'
import pathlib, struct, sys
png = (pathlib.Path(sys.argv[1])/'smoke/render/slides/slide-0001.png').read_bytes()
assert png[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',png[16:24]) == (1920,1080) and len(png)>10000
print('Linux PowerPoint renderer passed: 1920x1080')
PY
export ASPNETCORE_ENVIRONMENT=Production Authentication__Mode=ActiveDirectory
export ActiveDirectory__Host=str-dc-01.ad.strobertofnewminster.co.uk ActiveDirectory__DomainName=ad.strobertofnewminster.co.uk
export ActiveDirectory__UpnSuffix=str.bwcet.com ActiveDirectory__NetBiosDomain=STROBERTS ActiveDirectory__BaseDn=DC=ad,DC=strobertofnewminster,DC=co,DC=uk
export ActiveDirectory__BootstrapAdminUpn=''
export ConnectionStrings__SignageDb="Data Source=$stage/smoke/signage.db;Foreign Keys=True;Default Timeout=5"
export Database__InstanceLockPath="$stage/smoke/signage.lock" Storage__RootPath="$stage/smoke/content"
export Rendering__TempRoot="$stage/smoke/temp" Rendering__NodeExecutable=/opt/school-signage/runtime/node/bin/node
export Rendering__ConverterEntryPoint="$stage/package/converter/dist/cli.js" Rendering__WorkingDirectory="$stage/package/converter"
export Rendering__FontDirectories__0="$stage/package/fonts" Backup__RootPath="$stage/smoke/backups"
export AllowedHosts='localhost;127.0.0.1' Logging__LogLevel__Default=Warning
[[ -z $(ss -ltnH 'sport = :5198') ]] || { echo 'Smoke-test port is in use.' >&2; exit 1; }
# Build a disposable database with the currently installed schema, then upgrade it.
if [[ -f /opt/school-signage/current/app/Signage.Web.dll ]]; then
    /usr/bin/dotnet /opt/school-signage/current/app/Signage.Web.dll --migrate --contentRoot "$stage/package/app"
fi
/usr/bin/dotnet package/app/Signage.Web.dll --migrate --contentRoot "$stage/package/app"
python3 - "$stage/smoke/signage.db" <<'PY'
import sqlite3, sys
db = sqlite3.connect(sys.argv[1])
assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
assert db.execute('PRAGMA foreign_key_check').fetchall() == []
assert db.execute("SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId='20261006144313_LibraryManagement'").fetchone()
print('Existing schema upgrade and SQLite integrity passed')
PY
/usr/bin/dotnet package/app/Signage.Web.dll --contentRoot "$stage/package/app" --urls http://127.0.0.1:5198 >smoke/server.log 2>&1 &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true' EXIT
ready=false
for attempt in $(seq 1 45); do
    if [[ $(curl -fsS --max-time 3 -H 'X-Forwarded-Proto: https' http://127.0.0.1:5198/health/ready 2>/dev/null || true) == Healthy ]]; then ready=true; break; fi
    kill -0 "$server_pid" 2>/dev/null || { cat smoke/server.log; exit 1; }
    sleep 1
done
[[ $ready == true ]] || { cat smoke/server.log; echo 'Production readiness failed.' >&2; exit 1; }
for probe in '/health/live:200' '/dev-login:404' '/api/screen-groups/available:401' '/player/:200' '/Login:200' '/Admin:302'; do
    route=${probe%:*}; expected=${probe##*:}
    actual=$(curl -sS --max-time 10 -H 'X-Forwarded-Proto: https' -o "$stage/smoke/response.html" -w '%{http_code}' "http://127.0.0.1:5198$route")
    [[ $actual == "$expected" ]] || { echo "$route returned $actual, expected $expected" >&2; exit 1; }
    if [[ $route == /Login ]]; then grep -q 'type="password"' smoke/response.html; grep -q '/js/prefs.js' smoke/response.html; ! grep -q 'Development sign-in' smoke/response.html; fi
    printf 'Production probe passed: %s (%s)\n' "$route" "$actual"
done
for asset in js/prefs.js css/site.css js/site.js js/publish.js js/status.js; do
    curl -fsS --max-time 10 -H 'X-Forwarded-Proto: https' "http://127.0.0.1:5198/$asset" -o smoke/asset
    cmp "package/app/wwwroot/$asset" smoke/asset
done
printf '%s\n' "$archive_hash" >smoke/PASSED
printf 'Linux production validation passed at %s\n' "$(date -Is)"
