#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
# Never silently fall back to a local database when AWS was selected.
unset DynamoDb__ServiceUrl
export Storage__Provider=DynamoDb
export DynamoDb__TableName=suportehub-dev
export DynamoDb__Region=sa-east-1
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export Storage__KeysPath="${Storage__KeysPath:-$repo_root/.local/aws-keys}"
export Storage__UploadsPath="${Storage__UploadsPath:-$repo_root/.local/aws-uploads}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5218}"
cd "$repo_root/backend/SuporteHub.API"
exec dotnet bin/Debug/net10.0/SuporteHub.API.dll
