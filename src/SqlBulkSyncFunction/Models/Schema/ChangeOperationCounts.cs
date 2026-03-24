namespace SqlBulkSyncFunction.Models.Schema;

/// <summary>
/// Maps a single row returned by <c>SqlStatementExtensions.GetChangeTrackingOperationCountsSelectStatement</c> for Dapper materialization.
/// </summary>
public sealed class ChangeOperationCounts
{
    /// <summary>Count of <c>SYS_CHANGE_OPERATION = N'U'</c> rows.</summary>
    public long Updated { get; set; }

    /// <summary>Count of <c>SYS_CHANGE_OPERATION = N'I'</c> rows.</summary>
    public long Inserted { get; set; }

    /// <summary>Count of <c>SYS_CHANGE_OPERATION = N'D'</c> rows.</summary>
    public long Deleted { get; set; }
}
