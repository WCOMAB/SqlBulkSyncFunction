using System;
using System.Text.Json.Serialization;

namespace SqlBulkSyncFunction.Models.Job;

/// <summary>
/// Lightweight per-table row for <c>GET monitor/{area}/{id}</c>: current change-tracking versions only (no change counts).
/// </summary>
/// <param name="Id">Configured table identifier from the sync job definition.</param>
/// <param name="SourceTableName">Qualified source table name.</param>
/// <param name="SourceVersion">Current change tracking version on the source (<c>CHANGE_TRACKING_CURRENT_VERSION()</c> scope), or <c>-1</c> if unknown.</param>
/// <param name="TargetVersion">Last version stored in <c>sync.TableVersion</c> for the target table (or <c>-1</c> if never synced).</param>
/// <param name="TargetTableName">Qualified target table name.</param>
public record SyncJobMonitorTableVersionRow(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sourceTableName")] string SourceTableName,
    [property: JsonPropertyName("sourceVersion")] long SourceVersion,
    [property: JsonPropertyName("targetVersion")] long TargetVersion,
    [property: JsonPropertyName("queried")] DateTimeOffset Queried,
    [property: JsonPropertyName("updated")] DateTimeOffset? Updated,
    [property: JsonPropertyName("targetTableName")] string TargetTableName
);
