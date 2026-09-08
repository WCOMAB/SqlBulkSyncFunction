#!/usr/bin/env bash
# Per-boot service reconciliation: start Azurite (storage emulator) and the
# local SQL Server instance, wait until SQL accepts connections, and ensure the
# SyncTest demo database is present. Idempotent and safe to re-run.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/.npm-global/bin:/opt/mssql-tools18/bin:$PATH"
MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD:-Sync_Test_2026!}"

mkdir -p "$HOME/azurite-data"

echo "==> Starting Azurite"
if ! curl -s -o /dev/null "http://127.0.0.1:10000/devstoreaccount1"; then
  nohup azurite --silent --location "$HOME/azurite-data" \
    --blobPort 10000 --queuePort 10001 --tablePort 10002 \
    > /tmp/azurite.log 2>&1 &
fi

echo "==> Starting SQL Server"
if ! pgrep -x sqlservr >/dev/null 2>&1; then
  sudo -b -u mssql MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" \
    /opt/mssql/bin/sqlservr > /tmp/sqlservr.log 2>&1
fi

echo "==> Waiting for SQL Server to accept connections"
for i in $(seq 1 60); do
  if sqlcmd -S localhost,1433 -U sa -P "$MSSQL_SA_PASSWORD" -C -N o -l 2 \
       -Q "SELECT 1" >/dev/null 2>&1; then
    echo "SQL Server is ready"
    break
  fi
  sleep 2
done

echo "==> Ensuring SyncTest demo database"
sqlcmd -S localhost,1433 -U sa -P "$MSSQL_SA_PASSWORD" -C -N o \
  -i "$REPO_ROOT/.cursor/seed.sql" >/dev/null 2>&1 || \
  echo "WARN: seed.sql did not apply cleanly (SQL Server may still be starting)"

echo "==> start complete"
