#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
# The prepared cloud SDK is available without modifying user shell profiles.
if ! command -v dotnet >/dev/null 2>&1 && [[ -x /workspace/.dotnet/dotnet ]]; then
    export DOTNET_ROOT=/workspace/.dotnet
    export PATH="$DOTNET_ROOT:$PATH"
    export DOTNET_CLI_HOME=/workspace/.cloud-setup/dotnet-home
    export NUGET_PACKAGES=/workspace/.cloud-setup/nuget
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo "Instale o SDK .NET 10 antes de iniciar." >&2
    exit 1
fi
if [[ ! -f "$repo_root/backend/SuporteHub.API/bin/Debug/net10.0/SuporteHub.API.dll" || ! -f "$repo_root/backend/SuporteHub.API/bin/Debug/net10.0/wwwroot/index.html" ]]; then
    echo "Compile o projeto atualizado primeiro: dotnet build backend/SuporteHub.API/SuporteHub.API.csproj" >&2
    exit 1
fi
exec "$repo_root/scripts/run-local.sh"
