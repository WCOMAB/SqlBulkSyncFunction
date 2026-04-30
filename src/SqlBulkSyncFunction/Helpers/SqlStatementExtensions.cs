using System;
using System.Linq;
using SqlBulkSyncFunction.Models.Schema;
using SqlBulkSyncFunction.Models.Schema.Export;

namespace SqlBulkSyncFunction.Helpers;

public static class SqlStatementExtensions
{
    public static string GetTableVersionStatement(
        string tableName,
        bool globalChangeTracking,
        Column[] columns
        )
    {
        if (globalChangeTracking)
        {
            return  $"""
                    SELECT  '{tableName}'                                       AS TableName,
                            CHANGE_TRACKING_CURRENT_VERSION()                   AS CurrentVersion,
                            CHANGE_TRACKING_MIN_VALID_VERSION(
                                    ctt.object_id
                            )                                                   AS MinValidVersion,
                            SYSDATETIMEOFFSET()                                 AS Queried

                        FROM sys.change_tracking_tables AS ctt
                        WHERE ctt.object_id = OBJECT_ID('{tableName}')
                    """;
        }

        return string.Format(
            """
            SELECT  '{0}'                                               AS TableName,
                    SYS_CHANGE_VERSION                                  AS CurrentVersion,
                    CHANGE_TRACKING_MIN_VALID_VERSION(
                        OBJECT_ID('{0}')
                    )                                                   AS MinValidVersion,
                    SYSDATETIMEOFFSET()                                 AS Queried
                FROM  CHANGETABLE(VERSION  {0}, ({1}), ({1})) as t
            """,
            tableName,
            string.Join(
                ",",
                columns
                    .Where(column => column.IsPrimary)
                    .Select(column => column.QuoteName)
                )
            );
    }

    /// <summary>
    /// Builds a query that aggregates <c>CHANGETABLE</c> rows by <c>SYS_CHANGE_OPERATION</c>,
    /// producing <c>Updated</c>, <c>Inserted</c>, and <c>Deleted</c> counts (one row per table).
    /// Pass <c>@FromVersion</c> as <c>NULL</c> when no row exists in <c>sync.TableVersion</c> (never synced).
    /// </summary>
    /// <param name="sourceTableName">Fully qualified source table name (e.g. <c>[dbo].[MyTable]</c>).</param>
    public static string GetChangeTrackingOperationCountsSelectStatement(string sourceTableName)
        => $"""
            SELECT  ISNULL(SUM(CASE WHEN ct.SYS_CHANGE_OPERATION = N'U' THEN 1 ELSE 0 END), 0) AS Updated,
                    ISNULL(SUM(CASE WHEN ct.SYS_CHANGE_OPERATION = N'I' THEN 1 ELSE 0 END), 0) AS Inserted,
                    ISNULL(SUM(CASE WHEN ct.SYS_CHANGE_OPERATION = N'D' THEN 1 ELSE 0 END), 0) AS Deleted
                FROM CHANGETABLE(CHANGES {sourceTableName}, @FromVersion) AS ct
            """;

    /// <summary>
    /// Builds a query that returns only primary key values and operation code for each changed row from
    /// <c>CHANGETABLE(CHANGES ...)</c>. Result includes one row per change with operation in <c>Operation</c>.
    /// </summary>
    /// <param name="sourceTableName">Fully qualified source table name (e.g. <c>[dbo].[MyTable]</c>).</param>
    /// <param name="columns">Source table columns used to select primary key fields.</param>
    public static string GetChangeTrackingPrimaryKeyDetailsSelectStatement(string sourceTableName, Column[] columns)
    {
        var primaryColumns = columns
            .Where(column => column.IsPrimary)
            .Select(column => string.Concat("ct.", column.QuoteName, " AS ", column.QuoteName))
            .ToArray();

        if (primaryColumns.Length == 0)
        {
            throw new Exception($"Missing primary key columns for table {sourceTableName}.");
        }

        return $"""
            SELECT  ct.SYS_CHANGE_OPERATION AS Operation,
                    {string.Join(",\r\n        ", primaryColumns)}
                FROM CHANGETABLE(CHANGES {sourceTableName}, @FromVersion) AS ct
            """;
    }

    /// <summary>
    /// Builds a query for one export segment: insert/update rows join the live table for full column values;
    /// delete rows return primary key columns from <c>CHANGETABLE</c> only.
    /// <c>SYS_CHANGE_OPERATION</c> is not projected (segment already implies I/U/D) to reduce payload size.
    /// </summary>
    /// <param name="sourceTableName">Fully qualified source table name.</param>
    /// <param name="columns">Source columns (explicit list; no <c>*</c>).</param>
    /// <param name="segment">Which change operation to return.</param>
    /// <returns>Parameterized SQL using <c>@FromVersion</c>.</returns>
    public static string GetChangeTrackingExportSegmentSelectStatement(
        string sourceTableName,
        Column[] columns,
        SchemaTrackingExportSegment segment
        )
    {
        var primaryKeyColumns = columns.Where(column => column.IsPrimary).ToArray();
        if (primaryKeyColumns.Length == 0)
        {
            throw new InvalidOperationException($"Missing primary key columns for table {sourceTableName}.");
        }

        var operationFilter = segment switch
        {
            SchemaTrackingExportSegment.Updated => "N'U'",
            SchemaTrackingExportSegment.Inserted => "N'I'",
            SchemaTrackingExportSegment.Deleted => "N'D'",
            _ => throw new ArgumentOutOfRangeException(nameof(segment))
        };

        if (segment == SchemaTrackingExportSegment.Deleted)
        {
            var primarySelect = string.Join(
                ",\r\n        ",
                primaryKeyColumns.Select(column => string.Concat("ct.", column.QuoteName, " AS ", column.QuoteName))
            );

            return $"""
                SELECT  {primarySelect}
                    FROM CHANGETABLE(CHANGES {sourceTableName}, @FromVersion) AS ct
                    WHERE ct.SYS_CHANGE_OPERATION = {operationFilter}
                """;
        }

        var pkJoin = string.Join(
            " AND\r\n        ",
            primaryKeyColumns.Select(
                column => string.Concat("ct.", column.QuoteName, " = t.", column.QuoteName)
            )
        );

        var tableColumns = string.Join(
            ",\r\n        ",
            columns.Select(column => string.Concat("t.", column.QuoteName, " AS ", column.QuoteName))
        );

        return $"""
            SELECT  {tableColumns}
                FROM CHANGETABLE(CHANGES {sourceTableName}, @FromVersion) AS ct
                    INNER JOIN {sourceTableName} AS t ON {pkJoin}
                WHERE ct.SYS_CHANGE_OPERATION = {operationFilter}
            """;
    }

    public static string GetDropStatement(this string tableName)
        =>  $"""
            IF OBJECT_ID('{tableName}') IS NOT NULL
            BEGIN
                DROP TABLE {tableName}
            END
            """;

    public static string GetNewOrUpdatedMergeStatement(this TableSchema tableSchema, bool disableTargetIdentityInsert, bool disableConstraintCheck)
    {
        var identityInsert = !disableTargetIdentityInsert && tableSchema.Columns.Any(column => column.IsIdentity);
        var updatableColumns = tableSchema.Columns
            .Where(column => !column.IsPrimary && !column.IsIdentity)
            .ToArray();
        var whenMatchedBlock = updatableColumns.Length == 0
            ? string.Empty
            : string.Format(
                """
                WHEN MATCHED
                    THEN UPDATE
                        SET {0}
                """,
                string.Join(
                    ",\r\n            ",
                    updatableColumns
                        .Select(
                            column => string.Concat(
                                    column.QuoteName,
                                    " = source.",
                                    column.QuoteName
                                )
                        )
                    )
                );
        var statement = string.Format(
            """
            {0}
            {1}

            MERGE {2} AS target
            USING {3} AS source
            ON {4}
            WHEN NOT MATCHED BY TARGET
                THEN INSERT (
                    {5}
                ) VALUES (
                    {6}
                )
            {7}
            {8};

            SELECT @@ROWCOUNT AS [RowCount];

            {9}
            {10}
            """,
            (
                disableConstraintCheck
                    ? $"ALTER TABLE {tableSchema.TargetTableName} NOCHECK CONSTRAINT ALL;"
                    : string.Empty
                ),
            (
                identityInsert
                    ? $"SET IDENTITY_INSERT {tableSchema.TargetTableName} ON;"
                    : string.Empty
                ),
            tableSchema.TargetTableName,
            tableSchema.SyncNewOrUpdatedTableName,
            string.Join(
                " AND\r\n        ",
                tableSchema
                    .Columns
                        .Where(column => column.IsPrimary)
                        .Select(
                            column => string.Concat(
                                "target.",
                                column.QuoteName,
                                " = source.",
                                column.QuoteName
                                )
                            )
            ),
            string.Join(
                ",\r\n        ",
                tableSchema.Columns.Select(column => column.QuoteName)
            ),
            string.Join(
                ",\r\n        ",
                tableSchema.Columns.Select(column => string.Concat("source.", column.QuoteName))
            ),
            (
                (tableSchema.TargetVersion.CurrentVersion < 0)
                    ? string.Empty
                        //"""

                        //WHEN NOT MATCHED BY SOURCE
                        //    THEN DELETE
                        //"""
                    : string.Empty
            ),
            whenMatchedBlock,
            (
                identityInsert
                    ? $"SET IDENTITY_INSERT {tableSchema.TargetTableName} OFF;"
                    : string.Empty
                ),
            (
                disableConstraintCheck
                    ? $"ALTER TABLE {tableSchema.TargetTableName} CHECK CONSTRAINT ALL;"
                    : string.Empty
                )
            );
        return statement;
    }

    public static string GetDeleteStatement(this TableSchema tableSchema)
    {
        var statement = string.Format(
            """
            DELETE FROM target
            FROM {0} source
                INNER JOIN {1} target ON    {2}
            SELECT @@ROWCOUNT AS [RowCount]
            """,
            tableSchema.SyncDeletedTableName,
            tableSchema.TargetTableName,
            string.Join(
                " AND\r\n                                ",
                tableSchema.Columns.Where(column => column.IsPrimary).Select(
                    column => string.Concat(
                        "target.",
                        column.QuoteName,
                        " = source.",
                        column.QuoteName
                        )
                    )
                )
            );

        return statement;
    }

    public static string GetCreateNewOrUpdatedSyncTableStatement(this TableSchema tableSchema)
    {
        var statement = string.Format(
            """

            CREATE TABLE {0}(
                {1},
                CONSTRAINT [PK_{3}] PRIMARY KEY CLUSTERED
                (
                    {2}
                )
            )
            """,
            tableSchema.SyncNewOrUpdatedTableName,
            string.Join(
                ",\r\n    ",
                tableSchema.Columns.Select(
                    column => string.Join(
                        ' ',
                        column.QuoteName,
                        column.Type,
                        !string.IsNullOrEmpty(column.Collation) ? "COLLATE " + column.Collation : "",
                        column.IsNullable ? "NULL" : "NOT NULL"
                        )
                    )
                ),
            string.Join(
                ",\r\n    ",
                tableSchema.Columns
                    .Where(column => column.IsPrimary)
                    .Select(
                        column => column.QuoteName
                    )
                ),
            Guid.CreateVersion7()
            );
        return statement;
    }

    public static string GetCreateDeletedSyncTableStatement(this TableSchema tableSchema)
    {
        var statement = string.Format(
            """

            CREATE TABLE {0}(
                {1},
                CONSTRAINT [PK_{2}] PRIMARY KEY CLUSTERED
                (
                    {3}
                )
            )
            """,
            tableSchema.SyncDeletedTableName,
            string.Join(
                ",\r\n    ",
                tableSchema.Columns
                    .Where(column => column.IsPrimary)
                    .Select(
                    column => string.Join(
                        ' ',
                        column.QuoteName,
                        column.Type,
                        !string.IsNullOrEmpty(column.Collation) ? "COLLATE " + column.Collation : "",
                        column.IsNullable ? "NULL" : "NOT NULL"
                        )
                    )
                ),
            Guid.CreateVersion7(),
            string.Join(
                ",\r\n    ",
                tableSchema.Columns
                    .Where(column => column.IsPrimary)
                    .Select(
                        column => column.QuoteName
                    )
                )
            );
        return statement;
    }

    public static string GetDeletedAtSourceSelectStatement(this TableSchema tableSchema)
    {
        if (tableSchema.Columns == null || tableSchema.Columns.Length == 0)
        {
            throw new Exception(
                $"Columns for table {tableSchema.SourceTableName} missing ({tableSchema.Columns?.Length ?? -1})."
                );
        }

        var primaryKeyColumns = tableSchema.Columns
            .Where(column => column.IsPrimary)
            .ToArray();

        if (tableSchema.TargetVersion.CurrentVersion < 0)
        {
            return string.Format(
                """
                SELECT  TOP 0 {0}
                    FROM {1} WITH(NOLOCK)
                """,
                string.Join(
                    ",\r\n        ",
                    primaryKeyColumns.Select(column => column.QuoteName)
                    )
                ,
                tableSchema.SourceTableName
                );
        }

        return string.Format(
            """
            SELECT  {0}
                FROM CHANGETABLE(CHANGES {1}, {2}) ct
                WHERE ct.SYS_CHANGE_OPERATION = 'D'
            """,
            string.Join(
                ",\r\n        ",
                tableSchema.Columns
                .Where(column => column.IsPrimary)
                .Select(column => string.Concat("ct.", column.QuoteName))
                ),
            tableSchema.SourceTableName,
            tableSchema.TargetVersion.CurrentVersion
            );
    }

    public static string GetSourceSelectAllStatement(this TableSchema tableSchema)
        => string.Format(
            """
            SELECT  {0}
                FROM {1} WITH(NOLOCK)
                {2}
            """,
            string.Join(
                ",\r\n        ",
                tableSchema.Columns.Select(column => column.QuoteName)
            ),
            tableSchema.SourceTableName,
            tableSchema.Columns.Any(column => column.IsPrimary && column.IsIdentity)
                ? string.Concat(
                    "ORDER BY ",
                string.Join(
                    ",\r\n        ",
                    tableSchema.Columns
                        .Where(column => column.IsPrimary && column.IsIdentity)
                        .Select(column => string.Concat(column.QuoteName, " ASC"))
                    )
                )
                : string.Empty
        );

    public static string GetNewOrUpdatedAtSourceSelectStatement(this TableSchema tableSchema)
    {
        if (tableSchema.Columns == null || tableSchema.Columns.Length == 0)
        {
            throw new Exception(
                $"Columns for table {tableSchema.SourceTableName} missing ({tableSchema.Columns?.Length ?? -1})."
                );
        }

        var statement = (tableSchema.TargetVersion.CurrentVersion < 0)
            ? tableSchema.GetSourceSelectAllStatement()
            : string.Format(
                """
                SELECT  {0}
                    FROM CHANGETABLE(CHANGES {1}, {2}) ct
                        INNER JOIN {1} t WITH(NOLOCK) ON {3}
                """,
                string.Join(
                    ",\r\n        ",
                    tableSchema.Columns.Select(column => string.Concat("t.", column.QuoteName))
                    ),
                tableSchema.SourceTableName,
                tableSchema.TargetVersion.CurrentVersion,
                string.Join(
                    " AND\r\n        ",
                    tableSchema.Columns.Where(column => column.IsPrimary).Select(
                        column => string.Concat(
                            "t.",
                            column.QuoteName,
                            " = ct.",
                            column.QuoteName
                            )
                        )
                    )
                );
        return statement;
    }

    public static string GetTruncateTargetTableStatement(this TableSchema tableSchema)
        => string.Concat("TRUNCATE TABLE ", tableSchema.TargetTableName);

    public static string GetSyncTableExistStatement(this TableSchema tableSchema)
        =>  $"""
            DECLARE @True bit = 1,
                    @False bit = 0

            SELECT  CASE
                        WHEN OBJECT_ID('{tableSchema.SyncDeletedTableName}', 'U') IS NOT NULL THEN @True
                        WHEN OBJECT_ID('{tableSchema.SyncNewOrUpdatedTableName}', 'U') IS NOT NULL THEN @True
                        ELSE @False
                    END AS SyncTableExists
            """;
}
