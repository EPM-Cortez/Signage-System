#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

dotnet_bin="$repo_root/.dotnet/dotnet"
if [[ ! -x "$dotnet_bin" ]]; then dotnet_bin="$(command -v dotnet)"; fi
dotnet_version="$($dotnet_bin --version)"
if [[ ! "$dotnet_version" =~ ^10\. ]] || [[ "$dotnet_version" =~ (preview|rc) ]]; then
  echo "Stable .NET 10 is required. Found '$dotnet_version'." >&2; exit 1
fi
node_version="$(node --version)"
if [[ ! "$node_version" =~ ^v22\. ]]; then echo "Node.js 22 is required. Found '$node_version'." >&2; exit 1; fi

export DOTNET_CLI_HOME="$repo_root/.dotnet-home"
export NUGET_PACKAGES="$repo_root/.nuget/packages"
export npm_config_cache="$repo_root/.npm-cache"
export PLAYWRIGHT_BROWSERS_PATH="$repo_root/tests/Signage.E2ETests/.browsers"
export SIGNAGE_DOTNET="$dotnet_bin"
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$npm_config_cache" "$PLAYWRIGHT_BROWSERS_PATH" "$repo_root/.local-data/content" "$repo_root/.local-data/temp"

"$dotnet_bin" restore Signage.slnx --configfile "$repo_root/NuGet.Config" --locked-mode
"$dotnet_bin" tool restore --configfile "$repo_root/NuGet.Config"
(cd src/Signage.Converter && npm ci && npm run build)
(cd src/Signage.Player && npm ci && npm run build)
(cd tests/Signage.E2ETests && npm ci && npm exec -- playwright install chromium)
"$dotnet_bin" build Signage.slnx --no-restore
"$dotnet_bin" test Signage.slnx --no-build --no-restore
(cd src/Signage.Converter && npm test)
(cd src/Signage.Player && npm test)
(cd tests/Signage.E2ETests && npm test)
echo "Local setup complete (.NET $dotnet_version, Node $node_version)."
echo "Run ./run-local.sh and open http://localhost:5179/dev-login."
