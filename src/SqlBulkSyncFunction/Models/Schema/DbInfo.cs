namespace SqlBulkSyncFunction.Models.Schema;

/// <summary>
/// Database-level information including change tracking and server metadata.
/// </summary>
/// <param name="ServerName">Name of the SQL Server instance.</param>
/// <param name="DatabaseName">Name of the database.</param>
/// <param name="IsChangeTrackingDatabase">Whether change tracking is enabled for the database.</param>
/// <param name="IsAautoCleanupOn">Whether change tracking auto-cleanup is enabled.</param>
/// <param name="RetentionPeriod">Change tracking retention period value.</param>
/// <param name="RetentionPeriodUnit">Unit of the retention period (e.g. DAYS).</param>
/// <param name="ServerVersion">SQL Server version string.</param>
public record DbInfo(
    string ServerName,
    string DatabaseName,
    bool IsChangeTrackingDatabase,
    bool IsAautoCleanupOn,
    int? RetentionPeriod,
    string RetentionPeriodUnit,
    string ServerVersion
);
