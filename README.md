# SqlBulkSyncFunction [![Build Azure Function](https://github.com/WCOMAB/SqlBulkSyncFunction/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/WCOMAB/SqlBulkSyncFunction/actions/workflows/build.yml)

Azure Function version of [WCOMAB/SqlBulkSync](https://github.com/WCOMAB/SqlBulkSync) tool, a lightweight, performant non-intrusive SQL Server data sync service.

It doesn’t use any triggers or events, but instead uses the change tracking features available from SQL Server 2008 and up.
The service was developed primary for syncing on premise SQL server data to Azure in an efficient way, where only the changes are transferred. But it will also work just fine between on-premise/cloud instances too.

## Prerequisites

- .NET 5 SDK  https://www.microsoft.com/net/download
- Azure Functions Core Tools version 3.0.3381, or a later version. - https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local#v2
- Azure CLI version 2.20, or a later version. - https://docs.microsoft.com/en-us/cli/azure/install-azure-cli
- IDE
  - Visual Studio - 16.9.2, or a later version
  - VS Code - 1.54.3, or a later version

## Configuration

The function is configured through Azure App Settings / Environent variables, you can have multiple sync source/targets configures, and multiple tables per sync job.

| Key                                               | Description                               | Example                                                                      |
|---------------------------------------------------|-------------------------------------------|------------------------------------------------------------------------------|
| ProcessGlobalChangeTrackingSchedule               | Cron expression                           | `0 */5 * * * *`                                                              |
| SyncJobsConfig:Jobs:[key]:Source:ConnectionString | Source database connection string         | `Server=my.dbserver.net;Initial Catalog=MySourceDb;Integrated Security=True` |
| SyncJobsConfig:Jobs:[key]:Source:ManagedIdentity  | Flag for if managed identity used         | `false`                                                                      |
| SyncJobsConfig:Jobs:[key]:Source:TenantId         | Azure tenant ID used for managed identity | `46b41530-1e0d-4403-b815-24815944aa6a`                                       |
| SyncJobsConfig:Jobs:[key]:Target:ConnectionString | Source database connection string         | Server=my.dbserver.net;Initial Catalog=MySourceDb;Integrated Security=True   |
| SyncJobsConfig:Jobs:[key]:Target:ManagedIdentity  | Flag for if managed identity used         | `true`                                                                       |
| SyncJobsConfig:Jobs:[key]:Target:TenantId         | Azure tenant ID used for managed identity | `46b41530-1e0d-4403-b815-24815944aa6a`                                       |
| SyncJobsConfig:Jobs:[key]:BatchSize               | Bulk sync batch size                      | `1000`                                                                       |
| SyncJobsConfig:Jobs:[key]:Area                    | Area name, used to manually trigger sync  | `Development`                                                                |
| SyncJobsConfig:Jobs:[key]:Tables:[x]              | Table to sync                             | `dbo.MyTable`                                                                |

> Note:
> Replace `[key]` with unique name of sync job config i.e. `MySync`
> Replace `[x]` with index of table config starting with `0`
