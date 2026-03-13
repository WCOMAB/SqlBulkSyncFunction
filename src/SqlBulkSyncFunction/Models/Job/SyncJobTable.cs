namespace SqlBulkSyncFunction.Models.Job;

/// <summary>
/// Represents a table mapping for sync operations.
/// </summary>
/// <param name="Source">Source table name.</param>
/// <param name="Target">Target table name.</param>
/// <param name="DisableTargetIdentityInsert">Whether to disable identity insert on target table during sync.</param>
/// <param name="DisableConstraintCheck">Whether to disable constraint checking on target table during merge operations.</param>
public record SyncJobTable(
    string Source,
    string Target,
    bool DisableTargetIdentityInsert,
    bool DisableConstraintCheck
    );
