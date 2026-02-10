using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SqlBulkSyncFunction.Models.Job;

/// <summary>
/// Response DTO for a single table entry in sync job config.
/// </summary>
/// <param name="Source">Source table name.</param>
/// <param name="Target">Target table name.</param>
/// <param name="DisableTargetIdentityInsert">Whether target identity insert is disabled for this table.</param>
public record SyncJobConfigTableDto(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("disableTargetIdentityInsert")] bool DisableTargetIdentityInsert
);

/// <summary>
/// Response DTO for sync job config API.
/// </summary>
/// <param name="Id">Job identifier.</param>
/// <param name="Area">Job area.</param>
/// <param name="BatchSize">Optional batch size for sync operations.</param>
/// <param name="Manual">Whether the job is manual.</param>
/// <param name="Schedules">Schedule configuration.</param>
/// <param name="Tables">Table mappings (key -> table config).</param>
public record SyncJobConfigResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("area")] string Area,
    [property: JsonPropertyName("batchSize")] int? BatchSize,
    [property: JsonPropertyName("manual")] bool? Manual,
    [property: JsonPropertyName("schedules")] Dictionary<string, bool> Schedules,
    [property: JsonPropertyName("tables")] Dictionary<string, SyncJobConfigTableDto> Tables
);
