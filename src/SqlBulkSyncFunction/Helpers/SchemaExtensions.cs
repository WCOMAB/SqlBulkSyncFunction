using System;
using System.Data;
using System.Linq;
using Dapper;
using Microsoft.Data.SqlClient;
using SqlBulkSyncFunction.Models.Schema;

namespace SqlBulkSyncFunction.Helpers;

public static class SchemaExtensions
{
    /// <summary>
    /// Query to retrieve database-level info including change tracking and server metadata.
    /// </summary>
    public const string DbInfoQuery =
        """
        SELECT @@SERVERNAME                                         AS ServerName,
               DB_NAME(db.DatabaseId)                               AS DatabaseName,
               CAST(ISNULL(ct.IsChangeTrackingDatabase, 0) as bit)  AS IsChangeTrackingDatabase,
               CAST(ISNULL(IsAautoCleanupOn, 0) as bit)             AS IsAautoCleanupOn,
               RetentionPeriod                                      AS RetentionPeriod,
               RetentionPeriodUnit                                  AS RetentionPeriodUnit,
               @@VERSION                                            AS ServerVersion
        FROM (
                SELECT  DB_ID()                         AS DatabaseId   -- current DB
            ) db
            OUTER APPLY (
                SELECT  CAST(1 as bit)                  AS IsChangeTrackingDatabase,
                        is_auto_cleanup_on              AS IsAautoCleanupOn,
                        retention_period                AS RetentionPeriod,
                        retention_period_units_desc     AS RetentionPeriodUnit
                FROM sys.change_tracking_databases
                WHERE database_id = db.DatabaseId
            ) ct
        """;

    /// <summary>
    /// Query to retrieve change tracking metadata for all tracked tables in the database.
    /// Uses COUNT(*) per table so only SELECT on each table is required; no VIEW DATABASE PERFORMANCE STATE.
    /// EstimateRowCount is exact but may be slow on very large tables.
    /// </summary>
    public const string SourceTableChangeTrackingInfoQuery =
        """
        CREATE TABLE #Base (
            TableObjectId     INT NOT NULL,
            SchemaName        SYSNAME NOT NULL,
            TableName         SYSNAME NOT NULL,
            TrackColumnsUpdated BIT NOT NULL,
            MinValidVersion   BIGINT NOT NULL,
            CurrentDatabaseVersion BIGINT NOT NULL
        );
        INSERT #Base
        SELECT
            t.object_id                                     AS TableObjectId,
            s.name                                          AS SchemaName,
            t.name                                          AS TableName,
            CAST(ctt.is_track_columns_updated_on AS bit)    AS TrackColumnsUpdated,
            CHANGE_TRACKING_MIN_VALID_VERSION(t.object_id)  AS MinValidVersion,
            CHANGE_TRACKING_CURRENT_VERSION()               AS CurrentDatabaseVersion
        FROM sys.change_tracking_tables AS ctt
            JOIN sys.tables  AS t ON t.object_id = ctt.object_id
            JOIN sys.schemas AS s ON s.schema_id = t.schema_id
        ORDER BY s.name, t.name;

        CREATE TABLE #Counts (TableObjectId INT NOT NULL, EstimateRowCount BIGINT NULL);

        DECLARE @object_id INT, @schema SYSNAME, @table SYSNAME, @sql NVARCHAR(MAX);
        DECLARE c CURSOR LOCAL FAST_FORWARD FOR
            SELECT TableObjectId, SchemaName, TableName FROM #Base;
        OPEN c;
        FETCH NEXT FROM c INTO @object_id, @schema, @table;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            BEGIN TRY
                SET @sql = N'SELECT ' + CAST(@object_id as nvarchar(max)) + N' , COUNT(*)
                                FROM ' + QUOTENAME(@schema) + N'.' + QUOTENAME(@table) + 'WITH(NOLOCK)';
                INSERT INTO #Counts (TableObjectId, EstimateRowCount)
                    EXEC(@sql)
            END TRY
            BEGIN CATCH
                INSERT #Counts (TableObjectId, EstimateRowCount) VALUES (@object_id, NULL);
            END CATCH
            FETCH NEXT FROM c INTO @object_id, @schema, @table;
        END
        CLOSE c;
        DEALLOCATE c;

        SELECT
            b.TableObjectId             AS TableObjectId,
            b.SchemaName                AS SchemaName,
            b.TableName                 AS TableName,
            b.TrackColumnsUpdated       AS TrackColumnsUpdated,
            b.MinValidVersion           AS MinValidVersion,
            b.CurrentDatabaseVersion    AS CurrentDatabaseVersion,
            c.EstimateRowCount          AS EstimateRowCount
        FROM #Base b
            LEFT JOIN #Counts c ON c.TableObjectId = b.TableObjectId
        ORDER BY b.SchemaName, b.TableName;

        DROP TABLE #Counts;
        DROP TABLE #Base;
        """;

    public static void PersistsSourceTargetVersionState(
        this SqlConnection conn,
        TableSchema tableSchema
        )
    {
        var syncedTableVersion = tableSchema.SourceVersion with
        {
            TableName = tableSchema.TargetTableName
        };

        var persistedTableVersion = conn.Query<TableVersion>(
                commandTimeout: 180,
                param: syncedTableVersion,
                sql:
                """
                MERGE sync.TableVersion as target
                USING (
                    SELECT @TableName           AS TableName,
                        @CurrentVersion      AS CurrentVersion,
                        @MinValidVersion     AS MinValidVersion,
                        @Queried             AS Queried,
                        SYSDATETIMEOFFSET()  AS Updated,
                        SYSDATETIMEOFFSET()  AS Created
                ) as source
                ON target.TableName = source.TableName
                WHEN NOT MATCHED BY target
                    THEN INSERT (
                        TableName,
                        CurrentVersion,
                        MinValidVersion,
                        Queried,
                        Updated,
                        Created
                    )
                    VALUES (
                        source.TableName,
                        source.CurrentVersion,
                        source.MinValidVersion,
                        source.Queried,
                        source.Updated,
                        source.Created
                    )
                WHEN MATCHED THEN UPDATE
                    SET CurrentVersion  = source.CurrentVersion,
                        MinValidVersion = source.MinValidVersion,
                        Queried         = source.Queried,
                        Updated         = source.Updated

                OUTPUT  inserted.TableName,
                        inserted.CurrentVersion,
                        inserted.MinValidVersion,
                        inserted.Queried;
                """
            )
            .SingleOrDefault();

        if (persistedTableVersion != syncedTableVersion)
        {
            throw new Exception($"Failed to persist {syncedTableVersion} ({persistedTableVersion})");
        }
    }

    public static TableVersion GetTargetVersion(
        this SqlConnection conn,
        string tableName
        )
    {
        var tableVersion = conn.Query<TableVersion>(
                commandTimeout: 180,
                param: new
                {
                    TableName = tableName
                },
                sql:
                """
                SELECT  TableName,
                        CurrentVersion,
                        MinValidVersion,
                        Queried
                    FROM sync.TableVersion
                    WHERE TableName = @TableName
                """
            )
            .SingleOrDefault()
                ?? new TableVersion
                {
                    TableName = tableName,
                    CurrentVersion = -1,
                    MinValidVersion = -1
                };

        return tableVersion;
    }

    public static TableVersion GetSourceVersion(
        this SqlConnection conn,
        string tableName,
        bool globalChangeTracking,
        Column[] columns
        )
    {
        var tableVersionStatement = SqlStatementExtensions.GetTableVersionStatement(
            tableName,
            globalChangeTracking,
            columns
            );

        var result = conn.Query<TableVersion>(
            commandTimeout: 5000,
            sql: tableVersionStatement
            )
            .FirstOrDefault();
        return result;
    }

    public static Column[] GetColumns(this IDbConnection sourceConn, string tableName)
        => [.. sourceConn
            .Query<Column>(
                commandTimeout: 5000,
                param: new { TableName = tableName },
                sql: 
                """
                SELECT  c.Name              AS Name         ,
                        tn.Type             AS Type         ,
                        c.is_identity       AS IsIdentity   ,
                        tn.IsPrimary        AS IsPrimary    ,
                        c.is_nullable       AS IsNullable   ,
                        QUOTENAME(c.Name)   AS QuoteName    ,
                        c.collation_name    AS Collation
                    FROM sys.columns c
                        INNER JOIN sys.types tp ON c.user_type_id = tp.user_type_id
                        CROSS APPLY (
                            SELECT CASE
                                        WHEN tp.name IN('nvarchar')
                                            THEN    tp.name +
                                                    '(' +
                                                    CASE c.max_length
                                                        WHEN -1 THEN 'max'
                                                        ELSE CAST(c.max_length / 2 as nvarchar(max))
                                                    END
                                                    +')'
                                        WHEN tp.name IN('varchar')
                                            THEN    tp.name +
                                                    '(' +
                                                    CASE c.max_length
                                                        WHEN -1 THEN 'max'
                                                        ELSE CAST(c.max_length as varchar(max))
                                                    END
                                                    +')'
                                        WHEN tp.name = 'decimal'
                                            THEN    tp.name +
                                                    '(' +
                                                        CAST(c.precision as nvarchar(max))
                                                        + ', ' +
                                                        CAST(c.scale as nvarchar(max))
                                                    +')'
                                        ELSE tp.name
                                    END AS Type,
                                    CASE WHEN EXISTS(SELECT 1
                                                        FROM sys.indexes i
                                                            INNER JOIN sys.index_columns ic ON  i.object_id = ic.object_id  AND
                                                                                                i.index_id  = ic.index_id   AND
                                                                                                c.column_id = ic.column_id
                                                        WHERE   i.is_primary_key  = 1 AND
                                                                i.object_id = c.object_id)
                                        THEN CAST(1 AS bit)
                                        ELSE CAST(0 AS bit)
                                    END AS IsPrimary
                        ) tn
                WHERE   object_id       = object_id(@TableName) AND
                        c.is_computed   = 0 AND 
                        tp.name         <> 'timestamp'
                """
            )];
}
