namespace SqlBulkSyncFunction.Models.Schema;

/// <summary>
/// Change tracking metadata for a single table in the source database.
/// </summary>
/// <param name="TableObjectId">Object ID of the table.</param>
/// <param name="SchemaName">Schema name of the table.</param>
/// <param name="TableName">Name of the table.</param>
/// <param name="TrackColumnsUpdated">Whether column-level change tracking is enabled.</param>
/// <param name="MinValidVersion">Minimum valid change tracking version for the table.</param>
/// <param name="CurrentDatabaseVersion">Current change tracking version of the database.</param>
public record SourceTableChangeTrackingInfo(
    int TableObjectId,
    string SchemaName,
    string TableName,
    bool TrackColumnsUpdated,
    long MinValidVersion,
    long CurrentDatabaseVersion
);
