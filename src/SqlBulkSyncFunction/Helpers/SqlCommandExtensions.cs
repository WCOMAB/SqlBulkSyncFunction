using System;
using System.Data;
using System.Linq;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Models.Schema;

namespace SqlBulkSyncFunction.Helpers;

public static class SqlCommandExtensions
{
    public static void DropSyncTables(
        this SqlConnection targetConn,
        TableSchema tableSchema,
        object scope,
        ILogger logger
        ) => Array.ForEach(
            [
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
            ],
            table =>
            {
                if (string.IsNullOrEmpty(tableSchema?.SyncNewOrUpdatedTableName))
                {
                    return;
                }

                try
                {
                    _ = targetConn.Execute(
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
        try
        {
            var rowCount = targetConn.Query<long>(
                commandTimeout: 500000,
                sql: tableSchema.MergeNewOrUpdateStatement
                ).First();
            logger.LogInformation("{Scope} {RowCount} records merged", scope, rowCount);
        }
        catch(Exception ex)
        {
            logger.LogError(ex, "Merge failed for {Scope} with statement {MergeNewOrUpdateStatement}\r\n{Exception}", scope, tableSchema.MergeNewOrUpdateStatement, ex.Message);
            throw;
        }
    }

    public static void DeleteData(
        this SqlConnection targetConn,
        TableSchema tableSchema,
        object scope,
        ILogger logger
        )
    {
        var rowCount = targetConn.Query<long>(
            commandTimeout: 500000,
            sql: tableSchema.DeleteStatement
            ).First();
        logger.LogInformation("{Scope} {RowCount} records deleted.", scope, rowCount);
    }

    public static void BulkCopyData(
        this SqlConnection sourceConn,
        SqlConnection targetConn,
        TableSchema tableSchema,
        object scope,
        ILogger logger
        )
    {
        BulkCopyChangesSegment(
            sourceConn,
            targetConn,
            tableSchema,
            tableSchema.SyncNewOrUpdatedTableName,
            tableSchema.SourceNewOrUpdatedSelectStatement,
            tableSchema.Columns,
            scope,
            logger
            );

        BulkCopyChangesSegment(
            sourceConn,
            targetConn,
            tableSchema,
            tableSchema.SyncDeletedTableName,
            tableSchema.SourceDeletedSelectStatement,
            [.. tableSchema.Columns.Where(column => column.IsPrimary)],
            scope,
            logger
            );
    }

    private static void BulkCopyChangesSegment(
        SqlConnection sourceConn,
        SqlConnection targetConn,
        TableSchema tableSchema,
        string destinationTableName,
        string selectStatement,
        Column[] columnMappings,
        object scope,
        ILogger logger
        )
    {
        using var sourceCmd = new SqlCommand
        {
            Connection = sourceConn,
            CommandType = CommandType.Text,
            CommandText = selectStatement,
            CommandTimeout = 500000
        };

        WriteBulkCopy(
            sourceCmd,
            targetConn,
            tableSchema,
            destinationTableName,
            columnMappings,
            scope,
            logger
            );
    }

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
            [
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
            ],
            table =>
            {
                _ = targetConn.Execute(
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
            sql:
            """
            -- Validate Schema
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
                    Id              bigint IDENTITY(1,1)    NOT NULL PRIMARY KEY,
                    TableName       nvarchar(256)           NOT NULL,
                    CurrentVersion  bigint                  NOT NULL,
                    MinValidVersion bigint                  NOT NULL,
                    Queried         datetimeoffset(7)       NOT NULL,
                    Updated         datetimeoffset(7)       NOT NULL,
                    Created         datetimeoffset(7)       NOT NULL,
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
            END;
            """
        );

        logger.LogInformation("{Scope} {Result}", scope, qm.ReadFirst<string>());
        logger.LogInformation("{Scope} {Result}", scope, qm.ReadFirst<string>());
        logger.LogInformation("{Scope} {Result}", scope, qm.ReadFirst<string>());
    }

    public static void ClearTargetTable(
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
            CommandText = tableSchema.ClearTargetTableStatement,
            CommandTimeout = 500000
        };

        if (tableSchema.UseDeleteInsteadOfTruncate)
        {
            logger.LogInformation(
                "{Scope} Clearing table {TargetTableName} using DELETE (referencing tables: {ReferencingTableCount})...",
                scope,
                tableSchema.TargetTableName,
                tableSchema.ReferencingTables.Length
                );
        }
        else
        {
            logger.LogInformation("{Scope} Clearing table {TargetTableName} using TRUNCATE...", scope, tableSchema.TargetTableName);
        }

        _ = targetCmd.ExecuteNonQuery();
        logger.LogInformation("{Scope} Cleared table {TargetTableName}.", scope, tableSchema.TargetTableName);
    }

    public static void BulkCopyDataDirect(
        this SqlConnection sourceConn,
        SqlConnection targetConn,
        TableSchema tableSchema,
        object scope,
        ILogger logger
    )
    {
        using var transaction = tableSchema.UseSnapshotIsolationSeed
            ? sourceConn.BeginTransaction(IsolationLevel.Snapshot)
            : null;

        using var sourceCmd = new SqlCommand
        {
            Connection = sourceConn,
            CommandType = CommandType.Text,
            CommandText = tableSchema.SourceSelectAllStatement,
            CommandTimeout = 500000,
            Transaction = transaction
        };

        // Reader must be disposed before Commit; an open DataReader blocks the connection.
        WriteBulkCopy(
            sourceCmd,
            targetConn,
            tableSchema,
            tableSchema.TargetTableName,
            tableSchema.Columns,
            scope,
            logger
            );

        transaction?.Commit();
    }

    public static void BulkCopyAllToSyncNewOrUpdatedTable(
        this SqlConnection sourceConn,
        SqlConnection targetConn,
        TableSchema tableSchema,
        object scope,
        ILogger logger
    )
    {
        using var transaction = tableSchema.UseSnapshotIsolationSeed
            ? sourceConn.BeginTransaction(IsolationLevel.Snapshot)
            : null;

        using var sourceCmd = new SqlCommand
        {
            Connection = sourceConn,
            CommandType = CommandType.Text,
            CommandText = tableSchema.SourceSelectAllStatement,
            CommandTimeout = 500000,
            Transaction = transaction
        };

        WriteBulkCopy(
            sourceCmd,
            targetConn,
            tableSchema,
            tableSchema.SyncNewOrUpdatedTableName,
            tableSchema.Columns,
            scope,
            logger
            );

        transaction?.Commit();
    }

    public static void CreateNewOrUpdatedSyncTable(
        this SqlConnection targetConn,
        TableSchema tableSchema,
        object scope,
        ILogger logger
        )
    {
        _ = targetConn.Execute(
            commandType: CommandType.Text,
            commandTimeout: 500,
            sql: tableSchema.CreateNewOrUpdatedSyncTableStatement
            );
        logger.LogInformation("{Scope} Sync table {Name} created.", scope, tableSchema.SyncNewOrUpdatedTableName);
    }

    public static void DropNewOrUpdatedSyncTable(
        this SqlConnection targetConn,
        TableSchema tableSchema,
        object scope,
        ILogger logger
        )
    {
        try
        {
            _ = targetConn.Execute(
                commandType: CommandType.Text,
                commandTimeout: 500000,
                sql: tableSchema.DropNewOrUpdatedTableStatement
                );
            logger.LogInformation("{Scope} Sync table {Name} dropped.", scope, tableSchema.SyncNewOrUpdatedTableName);
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

    public static void ReconcileFullSyncData(
        this SqlConnection targetConn,
        TableSchema tableSchema,
        object scope,
        ILogger logger
        )
    {
        try
        {
            var result = targetConn.QueryFirst<(long Deleted, long Inserted, long Updated)>(
                commandTimeout: 500000,
                sql: tableSchema.FullSyncReconcileStatement
                );
            logger.LogInformation(
                "{Scope} Full sync reconcile complete. Deleted={Deleted}, Inserted={Inserted}, Updated={Updated}",
                scope,
                result.Deleted,
                result.Inserted,
                result.Updated
                );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Full sync reconcile failed for {Scope} with statement {FullSyncReconcileStatement}\r\n{Exception}",
                scope,
                tableSchema.FullSyncReconcileStatement,
                ex.Message
                );
            throw;
        }
    }

    private static void WriteBulkCopy(
        SqlCommand sourceCmd,
        SqlConnection targetConn,
        TableSchema tableSchema,
        string destinationTableName,
        Column[] columnMappings,
        object scope,
        ILogger logger
    )
    {
        using var reader = sourceCmd.ExecuteReader();

        using var bcp = new SqlBulkCopy(targetConn, SqlBulkCopyOptions.KeepIdentity, null)
        {
            DestinationTableName = destinationTableName,
            BatchSize = tableSchema.BatchSize,
            NotifyAfter = tableSchema.BatchSize,
            BulkCopyTimeout = 3600,
            EnableStreaming = true
        };

        foreach (var tableSchemaColumn in columnMappings)
        {
            _ = bcp.ColumnMappings.Add(
                tableSchemaColumn.Name,
                tableSchemaColumn.Name
            );

            if (tableSchemaColumn.IsPrimary && tableSchemaColumn.IsIdentity)
            {
                _ = bcp.ColumnOrderHints.Add(tableSchemaColumn.Name, SortOrder.Ascending);
            }
        }

        logger.LogInformation("{Scope} Bulk copy starting for {DestinationTableName}.", scope, destinationTableName);
        bcp.SqlRowsCopied += (s, e) => logger.LogInformation("{Scope} {DestinationTableName} {RowsCopied} rows copied", scope, destinationTableName, bcp.RowsCopied64);
        bcp.WriteToServer(reader);
        logger.LogInformation("{Scope} Bulk copy complete for {DestinationTableName}.", scope, destinationTableName);
    }
}
