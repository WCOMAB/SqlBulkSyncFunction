using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SqlBulkSyncFunction.Models.Job;

/// <summary>
/// One progress step in the latest (or selected) run for monitoring.
/// </summary>
/// <param name="State">Progress state name.</param>
/// <param name="Occured">When this state was recorded.</param>
/// <param name="Message">Optional details (e.g. exception).</param>
public record SyncJobMonitorProgressStepDto(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("occured")] DateTimeOffset Occured,
    [property: JsonPropertyName("message")] string Message
);

/// <summary>
/// Combined HTTP response for monitor endpoints: job identity, aggregated schedule/progress from blob, and live table versions.
/// Use <c>GET monitor/{area}</c> for an array for every job in the area (one per enabled schedule per job),
/// <c>GET monitor/{area}/{id}</c> for an array (one per enabled schedule), or <c>GET monitor/{area}/{id}/{schedule}</c> for one schedule.
/// </summary>
/// <param name="Area">Job area segment (same as the route).</param>
/// <param name="Id">Configured job identifier (same as the route).</param>
/// <param name="Schedule">Resolved schedule name for this payload (canonical key from config or <c>Manual</c>).</param>
/// <param name="ScheduleSummaryText">Human-readable schedule and timer summary from aggregated logs.</param>
/// <param name="ExpectedRunAt">Next timer run from the last aggregated schedule status (UTC), or an estimate from the same NCRONTAB as the global change-tracking timer when status is missing and the job has not started execution yet; null if unknown.</param>
/// <param name="LastRunAt">Last timer execution from the last aggregated schedule status (UTC); null if unknown.</param>
/// <param name="AggregatedAt">When the aggregate blob was last updated (if any).</param>
/// <param name="LatestRunCorrelationId">Correlation id of the run with the newest progress activity.</param>
/// <param name="LatestProgressSteps">Ordered steps for that run.</param>
/// <param name="Tables">Per-table source and target change-tracking versions only (no row counts).</param>
public record SyncJobMonitorResponse(
    [property: JsonPropertyName("area")] string Area,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("schedule")] string Schedule,
    [property: JsonPropertyName("scheduleSummaryText")] string ScheduleSummaryText,
    [property: JsonPropertyName("expectedRunAt")] DateTimeOffset? ExpectedRunAt,
    [property: JsonPropertyName("lastRunAt")] DateTimeOffset? LastRunAt,
    [property: JsonPropertyName("aggregatedAt")] DateTimeOffset? AggregatedAt,
    [property: JsonPropertyName("latestRunCorrelationId")] string LatestRunCorrelationId,
    [property: JsonPropertyName("latestProgressSteps")] IReadOnlyList<SyncJobMonitorProgressStepDto> LatestProgressSteps,
    [property: JsonPropertyName("tables")] IReadOnlyList<SyncJobMonitorTableVersionRow> Tables
);
