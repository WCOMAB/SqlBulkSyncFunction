namespace SqlBulkSyncFunction.Models.Schema;

/// <summary>
/// Per-table change tracking summary: counts of rows per <c>SYS_CHANGE_OPERATION</c> from
/// <c>CHANGETABLE(CHANGES …, last_sync_version)</c>, plus current source version and last synced target version.
/// </summary>
/// <param name="SourceTableName">Qualified source table name.</param>
/// <param name="Updated">Number of changes with <c>SYS_CHANGE_OPERATION = N'U'</c>.</param>
/// <param name="Inserted">Number of changes with <c>SYS_CHANGE_OPERATION = N'I'</c>.</param>
/// <param name="Deleted">Number of changes with <c>SYS_CHANGE_OPERATION = N'D'</c>.</param>
/// <param name="SourceVersion">Current change tracking version on the source (<c>CHANGE_TRACKING_CURRENT_VERSION()</c> for database-wide tracking).</param>
/// <param name="TargetVersion">Last version stored in <c>sync.TableVersion</c> for the target table (or <c>-1</c> if never synced).</param>
/// <param name="TargetTableName">Qualified target table name.</param>
public record SchemaTrackingTableRow(
    string SourceTableName,
    long Updated,
    long Inserted,
    long Deleted,
    long SourceVersion,
    long TargetVersion,
    string TargetTableName
);
