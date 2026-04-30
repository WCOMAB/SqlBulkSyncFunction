namespace SqlBulkSyncFunction.Models.Schema.Export;

#nullable enable

/// <summary>
/// Identifies which change-tracking operation bucket an export worker or ZIP file belongs to.
/// </summary>
public enum SchemaTrackingExportSegment
{
    /// <summary>
    /// Rows with <c>SYS_CHANGE_OPERATION = N'U'</c>.
    /// </summary>
    Updated = 0,

    /// <summary>
    /// Rows with <c>SYS_CHANGE_OPERATION = N'I'</c>.
    /// </summary>
    Inserted = 1,

    /// <summary>
    /// Rows with <c>SYS_CHANGE_OPERATION = N'D'</c> (primary key columns only).
    /// </summary>
    Deleted = 2
}
