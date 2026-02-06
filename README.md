# SqlBulkSyncFunction [![Build Azure Function](https://github.com/WCOMAB/SqlBulkSyncFunction/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/WCOMAB/SqlBulkSyncFunction/actions/workflows/build.yml)

Azure Function version of [WCOMAB/SqlBulkSync](https://github.com/WCOMAB/SqlBulkSync) tool, a lightweight, performant non-intrusive SQL Server data sync service.

It doesn’t use any triggers or events, but instead uses the change tracking features available from SQL Server 2008 and up.
The service was developed primary for syncing on premise SQL server data to Azure in an efficient way, where only the changes are transferred. But it will also work just fine between on-premise/cloud instances too.

## Prerequisites

- .NET 10 SDK - https://dotnet.microsoft.com/en-us/download
- Azure Functions Core Tools version 4.0, or a later version - https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local#v2
- Azure CLI version 2.50, or a later version - https://docs.microsoft.com/en-us/cli/azure/install-azure-cli
- IDE
  - Visual Studio 2022 17.10, or a later version
  - VS Code 1.90, or a later version

## Configuration

The function is configured through Azure App Settings / Environment variables, you can have multiple sync source/targets configures, and multiple tables per sync job.

| Key                                                                 | Description                               | Example                                                                      |
|---------------------------------------------------------------------|-------------------------------------------|------------------------------------------------------------------------------|
| `ProcessGlobalChangeTrackingSchedule`                               | Custom schedule cron expression           | `0 */5 * * * *`                                                              |
| `SyncJobsConfig__Jobs__[key]__Source__ConnectionString`             | Source database connection string         | `Server=my.dbserver.net;Initial Catalog=MySourceDb;Integrated Security=True` |
| `SyncJobsConfig__Jobs__[key]__Source__ManagedIdentity`              | Flag for if managed identity used         | `false`                                                                      |
| `SyncJobsConfig__Jobs__[key]__Source__TenantId`                     | Azure tenant ID used for managed identity | `46b41530-1e0d-4403-b815-24815944aa6a`                                       |
| `SyncJobsConfig__Jobs__[key]__Target__ConnectionString`             | Target database connection string         | `Server=my.dbserver.net;Initial Catalog=MyTargetDb;Integrated Security=True` |
| `SyncJobsConfig__Jobs__[key]__Target__ManagedIdentity`              | Flag for if managed identity used         | `true`                                                                       |
| `SyncJobsConfig__Jobs__[key]__Target__TenantId`                     | Azure tenant ID used for managed identity | `46b41530-1e0d-4403-b815-24815944aa6a`                                       |
| `SyncJobsConfig__Jobs__[key]__BatchSize`                            | Bulk sync batch size                      | `1000`                                                                       |
| `SyncJobsConfig__Jobs__[key]__Area`                                 | Area name, used to manually trigger sync  | `Development`                                                                |
| `SyncJobsConfig__Jobs__[key]__Manual`                               | Flag is sync excluded from schedules      | `true`                                                                       |
| `SyncJobsConfig__Jobs__[key]__Schedules__[key]`                     | Optional opt-in/out schedules             | `true`                                                                       |
| `SyncJobsConfig__Jobs__[key]__Tables__[key]`                        | Fully qualified name of table to sync     | `dbo.MyTable`                                                                |
| `SyncJobsConfig__Jobs__[key]__DisableTargetIdentityInsertTables__[key]` | Optional, per table. When `true`, merge does not use `IDENTITY_INSERT` on target (use when target has no identity column but source does). | `true` or `false` |


> Note:
>
> Replace `[key]` with unique name of sync job / table config i.e. `MySync` / `MyTable` would result in `SyncJobsConfig__Jobs__MySync__Tables__MyTable`=`dbo.MyTable`
>
> **DisableTargetIdentityInsertTables**: Omit or set to `false` to copy identity values from source (default). Set to `true` per table when the target schema has no identity column but the source does.
>
> Configuration from KeyVault replace `__` with `--` i.e. `SyncJobsConfig--Jobs--MySync--Tables--MyTable`

## Schedules

| Key                 | Description                                   | Cron expression                       |
|---------------------|-----------------------------------------------|---------------------------------------|
| `Custom`            | Triggers based on configured custom schedule  | `ProcessGlobalChangeTrackingSchedule` |
| `Midnight`          | Triggers `00:00 / 12AM` every day             | `0 0 0 * * *`                         |
| `Noon`              | Triggers `12:00 / 12PM` every day             | `0 0 12 * * *`                        |
| `EveryFiveMinutes`  | Triggers every five minutes                   | `5 */5 * * * *`                       |
| `EveryHour`         | Triggers every hour                           | `10 0 * * * *`                        |


> Note:
>
> `Custom` schedule is default if no scheduled is specified in sync job configuration.


## Development resources

To quicker get started testing the function locally example configuration and database schema provided below

### Example local.settings.json

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ProcessGlobalChangeTrackingSchedule": "0 23 11 * * *",
    "SyncJobsConfig__Jobs__SyncTest__Area": "SyncTest",
    "SyncJobsConfig__Jobs__SyncTest__Source__ConnectionString": "Server=localhost;Initial Catalog=SyncTest;Integrated Security=True",
    "SyncJobsConfig__Jobs__SyncTest__Source__ManagedIdentity": false,
    "SyncJobsConfig__Jobs__SyncTest__Target__ConnectionString": "Server=localhost;Initial Catalog=SyncTest;Integrated Security=True",
    "SyncJobsConfig__Jobs__SyncTest__Target__ManagedIdentity": false,
    "SyncJobsConfig__Jobs__SyncTest__BatchSize": 1000,
    "SyncJobsConfig__Jobs__SyncTest__Manual": false,
    "SyncJobsConfig__Jobs__SyncTest__Tables__Test": "source.[Test]",
    "SyncJobsConfig__Jobs__SyncTest__TargetTables__Test": "target.[Test]",
    "SyncJobsConfig__Jobs__SyncTest__Schedules__Custom": true,
    "SyncJobsConfig__Jobs__SyncTest__Schedules__Noon": true,
    "SyncJobsConfig__Jobs__SyncTest__Schedules__Midnight": true,
    "SyncJobsConfig__Jobs__SyncTest__Schedules__EveryFiveMinutes": true,
    "SyncJobsConfig__Jobs__SyncTest__Schedules__EveryHour": true
  }
}
```

### Example .env file for Docker

```
# Environment variables for running SqlBulkSyncFunction in Docker

AzureWebJobsStorage=UseDevelopmentStorage=true
FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
ProcessGlobalChangeTrackingSchedule=0 23 11 * * *
SyncJobsConfig__Jobs__SyncTest__Area=SyncTest
SyncJobsConfig__Jobs__SyncTest__Source__ConnectionString=Server=localhost;Initial Catalog=SyncTest;Integrated Security=True
SyncJobsConfig__Jobs__SyncTest__Source__ManagedIdentity=false
SyncJobsConfig__Jobs__SyncTest__Target__ConnectionString=Server=localhost;Initial Catalog=SyncTest;Integrated Security=True
SyncJobsConfig__Jobs__SyncTest__Target__ManagedIdentity=false
SyncJobsConfig__Jobs__SyncTest__BatchSize=1000
SyncJobsConfig__Jobs__SyncTest__Manual=false
SyncJobsConfig__Jobs__SyncTest__Tables__Test=source.[Test]
SyncJobsConfig__Jobs__SyncTest__TargetTables__Test=target.[Test]
SyncJobsConfig__Jobs__SyncTest__Schedules__Custom=true
SyncJobsConfig__Jobs__SyncTest__Schedules__Noon=true
SyncJobsConfig__Jobs__SyncTest__Schedules__Midnight=true
SyncJobsConfig__Jobs__SyncTest__Schedules__EveryFiveMinutes=true
SyncJobsConfig__Jobs__SyncTest__Schedules__EveryHour=true
```



### Example database seed script

Run the source script first (creates database and change tracking), then the target script.

**Source (database, schema, table, change tracking, seed data):**

```sql
-- Create Database
CREATE DATABASE [SyncTest]
GO
USE [SyncTest]
GO
ALTER DATABASE [SyncTest]
SET CHANGE_TRACKING = ON (
    CHANGE_RETENTION = 7 DAYS,
    AUTO_CLEANUP = ON
  )
GO

-- Create source schema and table
CREATE SCHEMA [source]
GO
CREATE TABLE [source].[Test](
  [Id] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [Description] [nvarchar](256) NULL,
  [Created] [datetime] NOT NULL
)
GO
ALTER TABLE [source].[Test] ENABLE CHANGE_TRACKING WITH(TRACK_COLUMNS_UPDATED = ON)
GO

-- Seed data
INSERT INTO source.[Test]
  (
    Description,
    Created
  ) VALUES (
    'Row 1',
    GETDATE()
  )
GO
INSERT INTO source.[Test]
  (
    Description,
    Created
  ) VALUES (
    'Row 2',
    GETDATE()
  )
```

**Target (schema and table):**

```sql
USE [SyncTest]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE SCHEMA [target]
GO
CREATE TABLE [target].[Test](
  [Id] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [Description] [nvarchar](256) NULL,
  [Created] [datetime] NOT NULL
)
GO
```

### Example permissions for sync

Grant the identity that runs the sync (e.g. managed identity or SQL login) the permissions below. Replace `[your-sync-identity]` with that principal.

**Source database (read and change tracking):**

```sql
USE [SyncTest]
GO

CREATE ROLE [SyncOperator]
GO
ALTER ROLE [SyncOperator] ADD MEMBER [your-sync-identity]
GO

-- Per table: change tracking, definition, and read
GRANT VIEW CHANGE TRACKING ON [source].[Test] TO [SyncOperator]
GO
GRANT VIEW DEFINITION ON [source].[Test] TO [SyncOperator]
GO
GRANT SELECT ON [source].[Test] TO [SyncOperator]
GO
```

**Target database (sync schema and table DML):**

```sql
USE [SyncTest]
GO

CREATE ROLE [SyncOperatorTarget]
GO
ALTER ROLE [SyncOperatorTarget] ADD MEMBER [your-sync-identity]
GO

-- Schema for sync metadata tables
CREATE SCHEMA [sync]
GO
GRANT SELECT, INSERT, DELETE, UPDATE, ALTER, CONTROL ON SCHEMA::[sync] TO [SyncOperatorTarget]
GO
ALTER AUTHORIZATION ON SCHEMA::[sync] TO [SyncOperatorTarget]
GO
GRANT CREATE TABLE TO [SyncOperatorTarget]
GO

-- Per target table: DML and ALTER (e.g. for sync metadata)
GRANT SELECT, INSERT, DELETE, UPDATE, ALTER ON [target].[Test] TO [SyncOperatorTarget]
GO
```

