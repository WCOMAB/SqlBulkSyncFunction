using System;
using System.Data;
using System.Linq;
using Dapper;
using Microsoft.Data.SqlClient;
using SqlBulkSyncFunction.Models.Schema;

namespace SqlBulkSyncFunction.Helpers
{
    public static class SchemaExtensions
    {
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
                    sql: @"MERGE sync.TableVersion as target
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
                            inserted.Queried;"
                ).SingleOrDefault();

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
                    param: new {
                        TableName = tableName
                    },
                    sql: @"SELECT    TableName,
                            CurrentVersion,
                            MinValidVersion,
                            Queried
                    FROM sync.TableVersion
                    WHERE TableName = @TableName"
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
        {
            return sourceConn
                .Query<Column>(
                    commandTimeout: 5000,
                    param: new { TableName = tableName },
                    sql: @"
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
            c.is_computed   = 0"
                )
                .ToArray();
        }
    }
}
