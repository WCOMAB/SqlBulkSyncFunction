using System;
using Microsoft.Data.SqlClient;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Models.Schema;

/// <summary>
/// Schema and pre-built SQL statements for syncing a single source/target table pair.
/// </summary>
public record TableSchema
{
    /// <summary>Human-readable scope label for logging.</summary>
    public string Scope { get; }

    /// <summary>Source table name.</summary>
    public string SourceTableName { get; }

    /// <summary>Target table name.</summary>
    public string TargetTableName { get; }

    /// <summary>Columns included in sync operations.</summary>
    public Column[] Columns { get; }

    /// <summary>Staging table for new or updated rows.</summary>
    public string SyncNewOrUpdatedTableName { get; }

    /// <summary>Staging table for deleted row keys.</summary>
    public string SyncDeletedTableName { get; }

    /// <summary>SQL to drop the new/updated staging table.</summary>
    public string DropNewOrUpdatedTableStatement { get; }

    /// <summary>SQL to drop the deleted staging table.</summary>
    public string DropDeletedTableStatement { get; }

    /// <summary>SQL merge statement for incremental sync.</summary>
    public string MergeNewOrUpdateStatement { get; }

    /// <summary>SQL delete statement for incremental sync.</summary>
    public string DeleteStatement { get; }

    /// <summary>SQL to select new/updated rows from source.</summary>
    public string SourceNewOrUpdatedSelectStatement { get; }

    /// <summary>SQL to select deleted row keys from source.</summary>
    public string SourceDeletedSelectStatement { get; }

    /// <summary>SQL to select all rows from source.</summary>
    public string SourceSelectAllStatement { get; }

    /// <summary>SQL to create the new/updated staging table.</summary>
    public string CreateNewOrUpdatedSyncTableStatement { get; }

    /// <summary>SQL to create the deleted staging table.</summary>
    public string CreateDeletedSyncTableStatement { get; }

    /// <summary>SQL batch to clear the target table during seed (TRUNCATE or DELETE, optional reseed).</summary>
    public string ClearTargetTableStatement { get; }

    /// <summary>SQL to detect whether sync staging tables already exist.</summary>
    public string SyncTableExistStatement { get; }

    /// <summary>Change tracking version from source.</summary>
    public TableVersion SourceVersion { get; }

    /// <summary>Persisted sync version on target.</summary>
    public TableVersion TargetVersion { get; }

    /// <summary>Bulk copy batch size.</summary>
    public int BatchSize { get; }

    /// <summary>Whether identity insert is disabled during merge.</summary>
    public bool DisableTargetIdentityInsert { get; }

    /// <summary>Whether constraint checking is disabled during merge.</summary>
    public bool DisableConstraintCheck { get; }

    /// <summary>When true, seed uses DELETE instead of TRUNCATE.</summary>
    public bool UseDeleteInsteadOfTruncate { get; }

    /// <summary>When true, seed runs DBCC CHECKIDENT after clearing the target.</summary>
    public bool ReseedTargetIdentityAfterClear { get; }

    /// <summary>Tables with foreign keys referencing the target (used for seed clear).</summary>
    public string[] ReferencingTables { get; }

    private TableSchema(
        SyncJobTable table,
        Column[] columns,
        TableVersion sourceVersion,
        TableVersion targetVersion,
        int? batchSize,
        string[] referencingTables,
        bool useDeleteInsteadOfTruncate
        )
    {
        var bufferName = table.Target.Replace("[", "").Replace("]", "");

        Scope = string.Concat(
            table.Source,
            " to ",
            table.Target
        );

        SourceTableName = table.Source;
        TargetTableName = table.Target;
        DisableTargetIdentityInsert = table.DisableTargetIdentityInsert;
        DisableConstraintCheck = table.DisableConstraintCheck;
        ReferencingTables = referencingTables;
        UseDeleteInsteadOfTruncate = useDeleteInsteadOfTruncate;
        ReseedTargetIdentityAfterClear = table.ReseedTargetIdentityAfterClear;
        SyncNewOrUpdatedTableName = FormattableString.Invariant($"sync.[{bufferName}_{DateTime.UtcNow:yyyyMMdd}_{targetVersion.CurrentVersion:00000000}_NewOrUpdated]");
        SyncDeletedTableName = FormattableString.Invariant($"sync.[{bufferName}_{DateTime.UtcNow:yyyyMMdd}_{targetVersion.CurrentVersion:00000000}_DeletedTable]");
        Columns = columns;
        SourceVersion = sourceVersion;
        TargetVersion = targetVersion;

        CreateNewOrUpdatedSyncTableStatement = this.GetCreateNewOrUpdatedSyncTableStatement();
        CreateDeletedSyncTableStatement = this.GetCreateDeletedSyncTableStatement();

        SourceNewOrUpdatedSelectStatement = this.GetNewOrUpdatedAtSourceSelectStatement();
        SourceSelectAllStatement = this.GetSourceSelectAllStatement();
        SourceDeletedSelectStatement = this.GetDeletedAtSourceSelectStatement();
        MergeNewOrUpdateStatement = this.GetNewOrUpdatedMergeStatement(DisableTargetIdentityInsert, DisableConstraintCheck);
        DeleteStatement = this.GetDeleteStatement();
        DropNewOrUpdatedTableStatement = SyncNewOrUpdatedTableName.GetDropStatement();
        DropDeletedTableStatement = SyncDeletedTableName.GetDropStatement();
        ClearTargetTableStatement = this.GetClearTargetTableStatement(UseDeleteInsteadOfTruncate, ReferencingTables);
        SyncTableExistStatement = this.GetSyncTableExistStatement();
        BatchSize = batchSize ?? 1000;
    }


    /// <summary>
    /// Loads schema metadata and pre-built SQL from source and target connections.
    /// </summary>
    public static TableSchema LoadSchema(
        SqlConnection sourceConn,
        SqlConnection targetConn,
        SyncJobTable syncTable,
        int? batchSize,
        bool globalChangeTracking
        )
    {
        var columns = sourceConn.GetColumns(syncTable.Source);
        var targetVersion = targetConn.GetTargetVersion(syncTable.Target);
        var referencingTables = targetConn.GetReferencingTables(syncTable.Target);
        var useDeleteInsteadOfTruncate = syncTable.DeleteInsteadOfTruncate || referencingTables.Length > 0;
        return new TableSchema(
            syncTable,
            columns,
            sourceConn.GetSourceVersion(syncTable.Source, globalChangeTracking, columns),
            targetVersion,
            batchSize,
            referencingTables,
            useDeleteInsteadOfTruncate
            );
    }
}
