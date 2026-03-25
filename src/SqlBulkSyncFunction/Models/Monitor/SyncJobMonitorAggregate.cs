using System;
using System.Collections.Generic;

namespace SqlBulkSyncFunction.Models.Monitor;

/// <summary>
/// Persisted snapshot for a single sync job and schedule (keyed by area, job id, and schedule name), updated by the monitoring aggregation timer.
/// </summary>
/// <param name="Area">Job area (matches <see cref="Models.Job.SyncJobConfig.Area"/>).</param>
/// <param name="JobId">Job configuration id (matches dictionary key in <see cref="Models.Job.SyncJobsConfig.Jobs"/>).</param>
/// <param name="Schedule">Schedule name (e.g. EveryFiveMinutes) that partitions this aggregate from other schedules for the same job.</param>
public sealed record SyncJobMonitorAggregate(
    string Area,
    string JobId,
    string Schedule
)
{
    /// <summary>UTC time when this aggregate was last written.</summary>
    public DateTimeOffset AggregatedAt { get; set; }

    /// <summary>Schedule name from the last processed <see cref="Models.LogSchedule"/> that listed this job.</summary>
    public string ScheduleName { get; set; }

    /// <summary>Correlation id of that schedule log blob.</summary>
    public string ScheduleCorrelationId { get; set; }

    /// <summary>Schedule log timestamp.</summary>
    public DateTimeOffset? ScheduleTimestamp { get; set; }

    /// <summary>Schedule expiry from the log entry.</summary>
    public DateTimeOffset? ScheduleExpires { get; set; }

    /// <summary>Last execution time from timer status when the schedule was logged.</summary>
    public DateTimeOffset? ScheduleLast { get; set; }

    /// <summary>Next scheduled execution from timer status when the schedule was logged.</summary>
    public DateTimeOffset? ScheduleNext { get; set; }

    /// <summary>Last-updated timestamp from timer status when the schedule was logged.</summary>
    public DateTimeOffset? ScheduleLastUpdated { get; set; }

    /// <summary>Whether the timer was past due when the schedule was logged.</summary>
    public bool IsSchedulePastDue { get; set; }

    /// <summary>Most recent progress <see cref="Models.SyncJobProgress.Occured"/> seen for this job.</summary>
    public DateTimeOffset? LastProgressOccured { get; set; }

    /// <summary><see cref="Models.SyncJob.SyncJobCorrelationId"/> (run id) for the latest progress event.</summary>
    public string LatestSyncJobCorrelationId { get; set; }

    /// <summary>Recent runs (newest activity first); size is capped by the aggregator.</summary>
    public List<SyncJobRunAggregate> Runs { get; set; } = [];
}
