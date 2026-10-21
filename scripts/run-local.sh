#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$repo_root/.local"
unset DynamoDb__ServiceUrl
export ASPNETCORE_ENVIRONMENT=Development
export Storage__Provider=Sqlite
export Storage__DatabasePath="$repo_root/.local/suportehub.db"
export Storage__KeysPath="$repo_root/.local/keys"
export Storage__UploadsPath="$repo_root/.local/uploads"
export Email__OutboxPath="$repo_root/.local/mail"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5218}"
cd "$repo_root/backend/SuporteHub.API"
exec dotnet bin/Debug/net10.0/SuporteHub.API.dll
