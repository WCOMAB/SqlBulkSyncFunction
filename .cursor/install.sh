#!/usr/bin/env bash
# Idempotent Cloud Agent bootstrap for SqlBulkSyncFunction.
# Installs the .NET SDK, Azure Functions Core Tools, Azurite and a local
# SQL Server 2022 instance, then restores/builds the project and generates a
# local.settings.json pointing at the local emulators. Safe to re-run.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FUNC_PROJ="$REPO_ROOT/src/SqlBulkSyncFunction"

DOTNET_VERSION="10.0.400"
DOTNET_DIR="$HOME/.dotnet"
NPM_PREFIX="$HOME/.npm-global"
# Local-only development credential for the throwaway SQL Server instance.
MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD:-Sync_Test_2026!}"

export DEBIAN_FRONTEND=noninteractive

echo "==> [1/6] .NET SDK $DOTNET_VERSION"
if ! "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --version "$DOTNET_VERSION" --install-dir "$DOTNET_DIR" --no-path
fi
export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$DOTNET_DIR/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "==> [2/6] Azure Functions Core Tools v4 + Azurite"
npm config set prefix "$NPM_PREFIX" >/dev/null 2>&1 || true
export PATH="$NPM_PREFIX/bin:$PATH"
command -v func >/dev/null 2>&1 || npm install -g azure-functions-core-tools@4
command -v azurite >/dev/null 2>&1 || npm install -g azurite

echo "==> [3/6] Local SQL Server 2022"
if [ ! -x /opt/mssql/bin/sqlservr ]; then
  curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
    | sudo tee /etc/apt/trusted.gpg.d/microsoft.asc >/dev/null
  curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list \
    | sudo tee /etc/apt/sources.list.d/mssql-server-2022.list >/dev/null
  curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/prod.list \
    | sudo tee /etc/apt/sources.list.d/mssql-prod.list >/dev/null
  sudo apt-get update -y
  sudo ACCEPT_EULA=Y apt-get install -y mssql-server mssql-tools18 unixodbc
fi
# SQL Server 2022 links against OpenLDAP 2.5, which Ubuntu 24.04 replaced with 2.6.
if ! ldconfig -p | grep -q 'liblber-2.5.so.0'; then
  tmp="$(mktemp -d)"
  curl -fsSL -o "$tmp/libldap25.deb" \
    "http://security.ubuntu.com/ubuntu/pool/main/o/openldap/libldap-2.5-0_2.5.20+dfsg-0ubuntu0.22.04.1_amd64.deb"
  dpkg-deb -x "$tmp/libldap25.deb" "$tmp/x"
  sudo cp -a "$tmp/x"/usr/lib/x86_64-linux-gnu/lib{lber,ldap}-2.5.so.0.1.15 /usr/lib/x86_64-linux-gnu/
  sudo ldconfig
  rm -rf "$tmp"
fi
# Initialise the instance (Developer edition) if it has never been configured.
# The data dir is root/mssql-only, so probe it with sudo to avoid re-running setup.
if ! sudo test -f /var/opt/mssql/data/master.mdf; then
  sudo MSSQL_PID=Developer MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" ACCEPT_EULA=Y \
    /opt/mssql/bin/mssql-conf -n setup accept-eula || true
fi

echo "==> [4/6] Restore & build"
dotnet restore "$FUNC_PROJ/SqlBulkSyncFunction.csproj"
dotnet build "$FUNC_PROJ/SqlBulkSyncFunction.csproj" -c Release

echo "==> [5/6] local.settings.json"
if [ ! -f "$FUNC_PROJ/local.settings.json" ]; then
  cat > "$FUNC_PROJ/local.settings.json" <<JSON
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "APPLICATIONINSIGHTS_CONNECTION_STRING": "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost/;LiveEndpoint=https://localhost/",
    "Logging__LogLevel__Default": "Information",
    "ProcessGlobalChangeTrackingSchedule": "0 23 11 * * *",
    "SyncJobsConfig__Jobs__SyncTest__Area": "SyncTest",
    "SyncJobsConfig__Jobs__SyncTest__Source__ConnectionString": "Server=localhost,1433;Initial Catalog=SyncTest;User Id=sa;Password=${MSSQL_SA_PASSWORD};Encrypt=True;TrustServerCertificate=True",
    "SyncJobsConfig__Jobs__SyncTest__Source__ManagedIdentity": false,
    "SyncJobsConfig__Jobs__SyncTest__Target__ConnectionString": "Server=localhost,1433;Initial Catalog=SyncTest;User Id=sa;Password=${MSSQL_SA_PASSWORD};Encrypt=True;TrustServerCertificate=True",
    "SyncJobsConfig__Jobs__SyncTest__Target__ManagedIdentity": false,
    "SyncJobsConfig__Jobs__SyncTest__BatchSize": 1000,
    "SyncJobsConfig__Jobs__SyncTest__Manual": false,
    "SyncJobsConfig__Jobs__SyncTest__Tables__Test": "source.[Test]",
    "SyncJobsConfig__Jobs__SyncTest__TargetTables__Test": "target.[Test]",
    "SyncJobsConfig__Jobs__SyncTest__Schedules__Custom": true,
    "SyncJobsConfig__Jobs__SyncTest__Schedules__EveryFiveMinutes": true
  }
}
JSON
fi

echo "==> [6/6] Persist tool paths for interactive shells"
MARK="# >>> sqlbulksync dev env >>>"
if ! grep -qF "$MARK" "$HOME/.bashrc" 2>/dev/null; then
  {
    echo "$MARK"
    echo 'export DOTNET_ROOT="$HOME/.dotnet"'
    echo 'export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$HOME/.npm-global/bin:/opt/mssql-tools18/bin:$PATH"'
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
    echo "# <<< sqlbulksync dev env <<<"
  } >> "$HOME/.bashrc"
fi

echo "==> install complete"
