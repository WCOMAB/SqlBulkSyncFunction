# SqlBulkSyncFunction [![Build Azure Function](https://github.com/WCOMAB/SqlBulkSyncFunction/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/WCOMAB/SqlBulkSyncFunction/actions/workflows/build.yml)

Azure Function version of [WCOMAB/SqlBulkSync](https://github.com/WCOMAB/SqlBulkSync) tool, a lightweight, performant non-intrusive SQL Server data sync service.

It doesn’t use any triggers or events, but instead uses the change tracking features available from SQL Server 2008 and up.
The service was developed primary for syncing on premise SQL server data to Azure in an efficient way, where only the changes are transferred. But it will also work just fine between on-premise/cloud instances too.

## Prerequisites

- .NET 7 SDK - https://dotnet.microsoft.com/en-us/download
- Azure Functions Core Tools version 4.0.4785, or a later version. - https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local#v2
- Azure CLI version 2.20, or a later version. - https://docs.microsoft.com/en-us/cli/azure/install-azure-cli
- IDE
  - Visual Studio - 17.4.2, or a later version
  - VS Code - 1.73.1, or a later version

## Configuration

The function is configured through Azure App Settings / Environment variables, you can have multiple sync source/targets configures, and multiple tables per sync job.

| Key                                                 | Description                               | Example                                                                      |
|-----------------------------------------------------|-------------------------------------------|------------------------------------------------------------------------------|
| `ProcessGlobalChangeTrackingSchedule`               | Custom schedule cron expression           | `0 */5 * * * *`                                                              |
| `SyncJobsConfig:Jobs:[key]:Source:ConnectionString` | Source database connection string         | `Server=my.dbserver.net;Initial Catalog=MySourceDb;Integrated Security=True` |
| `SyncJobsConfig:Jobs:[key]:Source:ManagedIdentity`  | Flag for if managed identity used         | `false`                                                                      |
| `SyncJobsConfig:Jobs:[key]:Source:TenantId`         | Azure tenant ID used for managed identity | `46b41530-1e0d-4403-b815-24815944aa6a`                                       |
| `SyncJobsConfig:Jobs:[key]:Target:ConnectionString` | Target database connection string         | `Server=my.dbserver.net;Initial Catalog=MyTargetDb;Integrated Security=True` |
| `SyncJobsConfig:Jobs:[key]:Target:ManagedIdentity`  | Flag for if managed identity used         | `true`                                                                       |
| `SyncJobsConfig:Jobs:[key]:Target:TenantId`         | Azure tenant ID used for managed identity | `46b41530-1e0d-4403-b815-24815944aa6a`                                       |
| `SyncJobsConfig:Jobs:[key]:BatchSize`               | Bulk sync batch size                      | `1000`                                                                       |
| `SyncJobsConfig:Jobs:[key]:Area`                    | Area name, used to manually trigger sync  | `Development`                                                                |
| `SyncJobsConfig:Jobs:[key]:Manual`                  | Flag is sync excluded from schedules      | `true`                                                                       |
| `SyncJobsConfig:Jobs:[key]:Schedules:[key]`         | Optional opt-in/out schedules             | `true`                                                                       |
| `SyncJobsConfig:Jobs:[key]:Tables:[key]`            | Fully qualified name of table to sync     | `dbo.MyTable`                                                                |


> Note:
>
> Replace `[key]` with unique name of sync job / table config i.e. `MySync` / `MyTable` would result in `SyncJobsConfig:Jobs:MySync:Tables:MyTable`=`dbo.MyTable`
>
> Non-Windows operating systems you'll need to replace `:` with `__`, i.e. `SyncJobsConfig__Jobs__MySync__Tables__MyTable`
>
> Configuration from KeyVault replace `:` with `--` i.e. `SyncJobsConfig--Jobs--MySync--Tables--MyTable`

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
    "SyncJobsConfig:Jobs:SyncTest:Area": "SyncTest",
    "SyncJobsConfig:Jobs:SyncTest:Source:ConnectionString": "Server=localhost;Initial Catalog=SyncTest;Integrated Security=True",
    "SyncJobsConfig:Jobs:SyncTest:Source:ManagedIdentity": false,
    "SyncJobsConfig:Jobs:SyncTest:Target:ConnectionString": "Server=localhost;Initial Catalog=SyncTest;Integrated Security=True",
    "SyncJobsConfig:Jobs:SyncTest:Target:ManagedIdentity": false,
    "SyncJobsConfig:Jobs:SyncTest:BatchSize": 1000,
    "SyncJobsConfig:Jobs:SyncTest:Manual": false,
    "SyncJobsConfig:Jobs:SyncTest:Tables:Test": "source.[Test]",
    "SyncJobsConfig:Jobs:SyncTest:TargetTables:Test": "target.[Test]",
    "SyncJobsConfig:Jobs:SyncTest:Schedules:Custom": true,
    "SyncJobsConfig:Jobs:SyncTest:Schedules:Noon": true,
    "SyncJobsConfig:Jobs:SyncTest:Schedules:Midnight": true,
    "SyncJobsConfig:Jobs:SyncTest:Schedules:EveryFiveMinutes": true,
    "SyncJobsConfig:Jobs:SyncTest:Schedules:EveryHour": true
  }
}
```

### Example database seed script

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

-- Create schemas
CREATE SCHEMA [source]
GO
CREATE SCHEMA [target]
GO

-- Create tables
CREATE TABLE [source].[Test](
  [Id] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [Description] [nvarchar](256) NULL,
  [Created] [datetime] NOT NULL
)
GO
ALTER TABLE [source].[Test] ENABLE CHANGE_TRACKING WITH(TRACK_COLUMNS_UPDATED = ON)
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [target].[Test](
  [Id] [bigint] IDENTITY(1,1) NOT NULL PRIMARY KEY,
  [Description] [nvarchar](256) NULL,
  [Created] [datetime] NOT NULL
)
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