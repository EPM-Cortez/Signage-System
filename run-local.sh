#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"
if [[ "${1:-}" != "--skip-setup" ]]; then "$repo_root/setup-local.sh"; fi
dotnet_bin="$repo_root/.dotnet/dotnet"
if [[ ! -x "$dotnet_bin" ]]; then dotnet_bin="$(command -v dotnet)"; fi
export DOTNET_CLI_HOME="$repo_root/.dotnet-home"
export NUGET_PACKAGES="$repo_root/.nuget/packages"
export ASPNETCORE_ENVIRONMENT=Development
exec "$dotnet_bin" run --project src/Signage.Web --no-build --no-restore --urls http://localhost:5179
