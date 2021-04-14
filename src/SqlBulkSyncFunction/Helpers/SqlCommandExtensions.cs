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
            ILogger logger
            )
        {
            Array.ForEach(
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
                        return;
                    try
                    {
                        targetConn.Execute(
                            commandType: CommandType.Text,
                            commandTimeout: 500000,
                            sql: table.DropStatement
                            );
                        logger.LogInformation("Sync table {0} dropped.", table.Name);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            "Failed to drop sync table {0}\r\n{1}",
                            tableSchema.SyncNewOrUpdatedTableName,
                            ex
                            );
                    }
                }
                );
        }

        public static void MergeData(
            this SqlConnection targetConn,
            TableSchema tableSchema,
            ILogger logger
            )
        {
            var rowCount = targetConn.Query<long>(
                commandTimeout: 500000,
                sql: tableSchema.MergeNewOrUpdateStatement
                ).First();
            logger.LogInformation("{0} records merged", rowCount);
        }

        public static void DeleteData(
            this SqlConnection targetConn,
            TableSchema tableSchema,
            ILogger Logger
            )
        {
            var rowCount = targetConn.Query<long>(
                commandTimeout: 500000,
                sql: tableSchema.DeleteStatement
                ).First();
            Logger.LogInformation("{0} records deleted", rowCount);
        }

        public static void BulkCopyData(
            this SqlConnection sourceConn,
            SqlConnection targetConn,
            TableSchema tableSchema,
            ILogger logger
            )
        {
            Array.ForEach(
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

                    using var bcp = new SqlBulkCopy(targetConn)
                    {
                        DestinationTableName = table.Name,
                        BatchSize = tableSchema.BatchSize,
                        NotifyAfter = tableSchema.BatchSize
                    };

                    bcp.SqlRowsCopied += (s, e) => logger.LogInformation("{0} {1} rows copied", e.RowsCopied, table.Description);
                    bcp.WriteToServer(reader);
                    logger.LogInformation("Bulk copy complete for {0}.", table.Description);
                }
                );
        }

        public static void CreateSyncTables(
            this SqlConnection targetConn,
            TableSchema tableSchema,
            ILogger logger
            )
        {
            Array.ForEach(
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
                    logger.LogInformation("Sync table {0} created.", table.Name, table.CreateStatement);
                }
                );
        }

        public static void EnsureSyncSchemaAndTableExists(
             this SqlConnection targetConn,
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

            logger.LogInformation(qm.ReadFirst<string>());
            logger.LogInformation(qm.ReadFirst<string>());
            logger.LogInformation(qm.ReadFirst<string>());
        }
    }
}
