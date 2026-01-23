using System;
using System.Data;
using System.Linq;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Models.Schema;

namespace SqlBulkSyncFunction.Helpers
{
    public static class SqlCommandExtensions
    {
        public static void DropSyncTables(
            this SqlConnection targetConn,
            TableSchema tableSchema,
            object scope,
            ILogger logger
            ) => Array.ForEach(
                new[]
                {
                    new
                    {
                        Name = tableSchema.SyncNewOrUpdatedTableName,
                        DropStatement = tableSchema.DropNewOrUpdatedTableStatement
                    },
                    new
                    {
                        Name = tableSchema.SyncDeletedTableName,
                        DropStatement = tableSchema.DropDeletedTableStatement
                    }
                },
                table =>
                {
                    if (string.IsNullOrEmpty(tableSchema?.SyncNewOrUpdatedTableName))
                    {
                        return;
                    }

                    try
                    {
                        targetConn.Execute(
                            commandType: CommandType.Text,
                            commandTimeout: 500000,
                            sql: table.DropStatement
                            );
                        logger.LogInformation("{Scope} Sync table {Name} dropped.", scope, table.Name);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "{Scope} Failed to drop sync table {SyncNewOrUpdatedTableName}\r\n{Exception}",
                            scope,
                            tableSchema.SyncNewOrUpdatedTableName,
                            ex.Message
                            );
                    }
                }
                );

        public static void MergeData(
            this SqlConnection targetConn,
            TableSchema tableSchema,
            object scope,
            ILogger logger
            )
        {
            var rowCount = targetConn.Query<long>(
                commandTimeout: 500000,
                sql: tableSchema.MergeNewOrUpdateStatement
                ).First();
            logger.LogInformation("{Scope} {RowCount} records merged", scope, rowCount);
        }

        public static void DeleteData(
            this SqlConnection targetConn,
            TableSchema tableSchema,
            object scope,
            ILogger Logger
            )
        {
            var rowCount = targetConn.Query<long>(
                commandTimeout: 500000,
                sql: tableSchema.DeleteStatement
                ).First();
            Logger.LogInformation("{Scope} {RowCount} records deleted.", scope, rowCount);
        }

        public static void BulkCopyData(
            this SqlConnection sourceConn,
            SqlConnection targetConn,
            TableSchema tableSchema,
            object scope,
            ILogger logger
            ) => Array.ForEach(
                new[]
                {
                    new
                    {
                        Name = tableSchema.SyncNewOrUpdatedTableName,
                        SelectStatement = tableSchema.SourceNewOrUpdatedSelectStatement,
                        Description = "new or updated"
                    },
                    new
                    {
                        Name = tableSchema.SyncDeletedTableName,
                        SelectStatement = tableSchema.SourceDeletedSelectStatement,
                        Description = "deleted"
                    }
                },
                table =>
                {
                    using var sourceCmd = new SqlCommand
                    {
                        Connection = sourceConn,
                        CommandType = CommandType.Text,
                        CommandText = table.SelectStatement,
                        CommandTimeout = 500000
                    };

                    using var reader = sourceCmd.ExecuteReader();

                    using var bcp = new SqlBulkCopy(targetConn, SqlBulkCopyOptions.KeepIdentity, null)
                    {
                        DestinationTableName = table.Name,
                        BatchSize = tableSchema.BatchSize,
                        NotifyAfter = tableSchema.BatchSize,
                        BulkCopyTimeout = 300,
                        EnableStreaming = true
                    };

                    bcp.SqlRowsCopied += (s, e) => logger.LogInformation("{Scope} {TableName} {RowsCopied} {Description} rows copied", scope, table.Name, e.RowsCopied, table.Description);
                    bcp.WriteToServer(reader);
                    logger.LogInformation("{Scope} Bulk copy complete for {Description}.", scope, table.Description);
                }
                );

        public static bool SyncTablesExist(
            this SqlConnection targetConn,
            TableSchema tableSchema
            ) => targetConn.Query<bool>(
                        commandType: CommandType.Text,
                        commandTimeout: 500,
                        sql: tableSchema.SyncTableExistStatement
                        ).First();

        public static void CreateSyncTables(
            this SqlConnection targetConn,
            TableSchema tableSchema,
            object scope,
            ILogger logger
            ) => Array.ForEach(
                new[]
                {
                    new
                    {
                        Name = tableSchema.SyncNewOrUpdatedTableName,
                        CreateStatement = tableSchema.CreateNewOrUpdatedSyncTableStatement
                    },
                    new
                    {
                        Name = tableSchema.SyncDeletedTableName,
                        CreateStatement = tableSchema.CreateDeletedSyncTableStatement
                    }
                },
                table =>
                {
                    targetConn.Execute(
                        commandType: CommandType.Text,
                        commandTimeout: 500,
                        sql: table.CreateStatement
                        );
                    logger.LogInformation("{Scope} Sync table {Name} created.", scope, table.Name);
                }
                );

        public static void EnsureSyncSchemaAndTableExists(
            this SqlConnection targetConn,
            object scope,
            ILogger logger
        )
        {
            using var qm = targetConn.QueryMultiple(
                commandTimeout: 5000,
                sql: @"-- Validate Schema
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'sync')
BEGIN
	EXEC sys.sp_executesql N'CREATE SCHEMA sync'
	SELECT 'Schema sync created' AS Message
END
ELSE
BEGIN
	SELECT 'Schema sync exists' AS Message
END;
-- Validate Table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'sync.TableVersion') AND type in (N'U'))
BEGIN
	CREATE TABLE sync.TableVersion(
		Id				bigint IDENTITY(1,1)	NOT NULL PRIMARY KEY,
		TableName		nvarchar(256)			NOT NULL,
		CurrentVersion	bigint					NOT NULL,
		MinValidVersion bigint					NOT NULL,
		Queried			datetimeoffset(7)		NOT NULL,
		Updated			datetimeoffset(7)		NOT NULL,
		Created			datetimeoffset(7)		NOT NULL,
	)
	SELECT 'Table sync.TableVersion created' AS Message
END
ELSE
BEGIN
	SELECT 'Table sync.TableVersion exists' AS Message
END;
-- Validate Index
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'sync.TableVersion') AND name = N'IX_sync_TableVersion_TableName')
BEGIN
	CREATE NONCLUSTERED INDEX IX_sync_TableVersion_TableName ON sync.TableVersion
	(
		TableName ASC
	) INCLUDE(
		CurrentVersion,
		MinValidVersion,
		Queried
	)
	SELECT 'Index IX_sync_TableVersion_TableName created' AS Message
END
ELSE
BEGIN
	SELECT 'Index IX_sync_TableVersion_TableName exists' AS Message
END;"
            );

            logger.LogInformation("{Scope} {Result}", scope, qm.ReadFirst<string>());
            logger.LogInformation("{Scope} {Result}", scope, qm.ReadFirst<string>());
            logger.LogInformation("{Scope} {Result}", scope, qm.ReadFirst<string>());
        }

        public static void TruncateTargetTable(
            this SqlConnection targetConn,
            TableSchema tableSchema,
            object scope,
            ILogger logger
        )
        {
            using var targetCmd = new SqlCommand
            {
                Connection = targetConn,
                CommandType = CommandType.Text,
                CommandText = tableSchema.TruncateTargetTableStatement,
                CommandTimeout = 500000
            };
            logger.LogInformation("{Scope} Truncating table {TargetTableName}...", scope, tableSchema.TargetTableName);
            targetCmd.ExecuteNonQuery();
            logger.LogInformation("{Scope} Truncated table {TargetTableName}.", scope, tableSchema.TargetTableName);
        }

        public static void BulkCopyDataDirect(
            this SqlConnection sourceConn,
            SqlConnection targetConn,
            TableSchema tableSchema,
            object scope,
            ILogger logger
        )
        {
            using var sourceCmd = new SqlCommand
            {
                Connection = sourceConn,
                CommandType = CommandType.Text,
                CommandText = tableSchema.SourceSelectAllStatement,
                CommandTimeout = 500000
            };

            using var reader = sourceCmd.ExecuteReader();

            using var bcp = new SqlBulkCopy(targetConn, SqlBulkCopyOptions.KeepIdentity, null)
            {
                DestinationTableName = tableSchema.TargetTableName,
                BatchSize = tableSchema.BatchSize,
                NotifyAfter = tableSchema.BatchSize,
                BulkCopyTimeout = 600,
                EnableStreaming = true
            };

            foreach (var tableSchemaColumn in tableSchema.Columns)
            {
                bcp.ColumnMappings.Add(
                    tableSchemaColumn.Name,
                    tableSchemaColumn.Name
                );

                if (tableSchemaColumn.IsPrimary && tableSchemaColumn.IsIdentity)
                {
                    bcp.ColumnOrderHints.Add(tableSchemaColumn.Name, SortOrder.Ascending);
                }
            }

            logger.LogInformation("{Scope} Bulk copy starting for {TargetTableName}.", scope, tableSchema.TargetTableName);
            bcp.SqlRowsCopied += (s, e) => logger.LogInformation("{Scope} {TargetTableName} {RowsCopied} rows copied", scope, tableSchema.TargetTableName, e.RowsCopied);
            bcp.WriteToServer(reader);
            logger.LogInformation("{Scope} Bulk copy complete for {TargetTableName}.", scope, tableSchema.TargetTableName);
        }
    }
}
