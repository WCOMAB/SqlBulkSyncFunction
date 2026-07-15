namespace SqlBulkSyncFunction.Models.Job;

/// <summary>
/// Represents a table mapping for sync operations.
/// </summary>
/// <param name="Id">Identifier for the table mapping in configuration.</param>
/// <param name="Source">Source table name.</param>
/// <param name="Target">Target table name.</param>
/// <param name="DisableTargetIdentityInsert">Whether to disable identity insert on target table during sync.</param>
/// <param name="DisableConstraintCheck">Whether to disable constraint checking on target table during merge operations.</param>
/// <param name="DeleteInsteadOfTruncate">Whether to use DELETE instead of TRUNCATE when clearing target during seed.</param>
/// <param name="ReseedTargetIdentityAfterClear">Whether to run DBCC CHECKIDENT after clearing the target during seed.</param>
public record SyncJobTable(
    string Id,
    string Source,
    string Target,
    bool DisableTargetIdentityInsert,
    bool DisableConstraintCheck,
    bool DeleteInsteadOfTruncate,
    bool ReseedTargetIdentityAfterClear
    );
