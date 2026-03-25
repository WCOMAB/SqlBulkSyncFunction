using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Models.Monitor;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions;

/// <summary>
/// HTTP endpoints for sync job monitoring (aggregated schedule/progress and lightweight table versions).
/// </summary>
public sealed class GetSyncJobMonitor(
    ILogger<GetSyncJobMonitor> logger,
    IOptions<SyncJobsConfig> syncJobsConfig,
    ITokenCacheService tokenCacheService,
    SyncMonitoringAggregationService syncMonitoringAggregationService,
    ProcessGlobalChangeTrackingScheduleNextRun processGlobalChangeTrackingScheduleNextRun
    )
{
    private static readonly string[] ProgressStateOrder = Enum.GetNames<SyncJobProgressState>();

    /// <summary>
    /// <c>GET monitor/{area}</c> returns all enabled schedules for every job in the area as a JSON array.
    /// <c>GET monitor/{area}/{id}</c> returns all enabled schedules for that job as a JSON array.
    /// <c>GET monitor/{area}/{id}/{schedule}</c> returns a JSON array with one <see cref="SyncJobMonitorResponse"/> for that schedule.
    /// </summary>
    [Function(nameof(GetSyncJobMonitor))]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route = "monitor/{area}/{id?}/{schedule?}"
        )]
        HttpRequest req,
        string area,
        string id,
        string schedule,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(req);

        if (string.IsNullOrWhiteSpace(area))
        {
            return new NotFoundResult();
        }

        var jobs = syncJobsConfig?.Value?.Jobs?
            .Where(kv =>
                kv.Value != null
                && kv.Value.Area is { Length: > 0 }
                && StringComparer.OrdinalIgnoreCase.Equals(area, kv.Value.Area)
                && (string.IsNullOrWhiteSpace(id) || StringComparer.OrdinalIgnoreCase.Equals(kv.Key, id)))
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (jobs == null || jobs.Count == 0)
        {
            return new NotFoundResult();
        }

        var utcNow = DateTimeOffset.UtcNow;

        var responses = await EnumerateMonitorResponsesForJobsAsync(
                jobs,
                area,
                utcNow,
                schedule,
                cancellationToken
            )
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OkObjectResult(responses);
    }

    /// <summary>
    /// For each matching job, loads table versions once and yields one <see cref="SyncJobMonitorResponse"/> per schedule
    /// from <see cref="EnumerateSchedulesForJob{T}"/> (optionally filtered by <paramref name="schedule"/>).
    /// </summary>
    private async IAsyncEnumerable<SyncJobMonitorResponse> EnumerateMonitorResponsesForJobsAsync(
        IReadOnlyList<KeyValuePair<string, SyncJobConfig>> jobs,
        string area,
        DateTimeOffset utcNow,
        string schedule,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var (jobId, jobConfig) in jobs)
        {
            var syncJob = jobConfig.ToSyncJob(
                scheduleCorrelationId: null,
                tokenCache: await tokenCacheService.GetTokenCache(jobConfig).ConfigureAwait(false),
                timestamp: utcNow,
                expires: utcNow.AddMinutes(4),
                id: jobId,
                schedule: nameof(jobConfig.Manual),
                seed: false
            );

            var tableRows = await LoadMonitorTableVersionsAsync(syncJob, logger, cancellationToken)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            await foreach (
                var response in EnumerateSchedulesForJob(
                    jobConfig,
                    async resolvedSchedule => await BuildMonitorResponseAsync(
                            area,
                            jobId,
                            resolvedSchedule,
                            tableRows,
                            utcNow,
                            cancellationToken
                        )
                        .ConfigureAwait(false),
                    schedule
                )
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return response;
            }
        }
    }

    private async Task<SyncJobMonitorResponse> BuildMonitorResponseAsync(
        string area,
        string id,
        string resolvedSchedule,
        IReadOnlyList<SyncJobMonitorTableVersionRow> tableRows,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken
    )
    {
        var aggregate = await syncMonitoringAggregationService
            .GetAggregateAsync(area, id, resolvedSchedule, cancellationToken)
            .ConfigureAwait(false);

        var scheduleSummaryText = BuildScheduleSummaryText(aggregate);

        var expectedRunAt = aggregate?.ScheduleNext;
        if (!expectedRunAt.HasValue
            && !string.Equals(resolvedSchedule, "Manual", StringComparison.OrdinalIgnoreCase)
            && HasSyncJobNotStartedExecution(aggregate)
            && processGlobalChangeTrackingScheduleNextRun.TryGetNextRunUtc(
                resolvedSchedule,
                utcNow,
                out var estimatedNext))
        {
            expectedRunAt = estimatedNext;
        }

        return new SyncJobMonitorResponse(
            Area: area,
            Id: id,
            Schedule: resolvedSchedule,
            ScheduleSummaryText: scheduleSummaryText,
            ExpectedRunAt: expectedRunAt,
            LastRunAt: aggregate?.ScheduleTimestamp ?? aggregate?.ScheduleLast,
            AggregatedAt: aggregate?.AggregatedAt,
            LatestRunCorrelationId: aggregate?.LatestSyncJobCorrelationId,
            LatestProgressSteps: [.. BuildLatestProgressSteps(aggregate)],
            Tables: tableRows
        );
    }

    /// <summary>
    /// Enabled schedule names for this job (same rules as <see cref="SyncJobsConfig"/> scheduled jobs).
    /// When <paramref name="schedule"/> is non-empty, only the schedule that <see cref="ResolveScheduleForJob"/> accepts is enumerated (zero or one item).
    /// </summary>
    private static async IAsyncEnumerable<T> EnumerateSchedulesForJob<T>(
        SyncJobConfig job,
        Func<string, Task<T>> selector,
        string schedule = null
        )
    {
        if (!string.IsNullOrWhiteSpace(schedule))
        {
            var resolved = ResolveScheduleForJob(job, schedule);
            if (resolved != null)
            {
                yield return await selector(resolved);
            }

            yield break;
        }

        if (job.Manual == true)
        {
            yield return await selector("Manual");
            yield break;
        }

        if (job.Schedules != null && job.Schedules.Count > 0)
        {
            foreach (
                var key in job.Schedules
                    .Where(p => p.Value)
                    .Select(p => p.Key)
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                yield return await selector(key);
            }

            yield break;
        }
    }

    /// <summary>
    /// Returns the canonical schedule name from the job config for the requested segment, or null if invalid.
    /// </summary>
    private static string ResolveScheduleForJob(SyncJobConfig job, string schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            return null;
        }

        if (job.Manual == true)
        {
            return string.Equals(schedule, "Manual", StringComparison.OrdinalIgnoreCase) ? "Manual" : null;
        }

        if (job.Schedules != null && job.Schedules.Count > 0)
        {
            foreach (var key in job.Schedules.Keys)
            {
                if (string.Equals(key, schedule, StringComparison.OrdinalIgnoreCase))
                {
                    return job.Schedules[key] ? key : null;
                }
            }
        }

        return null;
    }

    private static string BuildScheduleSummaryText(SyncJobMonitorAggregate aggregate)
    {
        if (aggregate == null)
        {
            return "No aggregated schedule data yet; wait for the monitoring timer to process queues.";
        }

        var name = aggregate.ScheduleName ?? "unknown";
        var pastDue = aggregate.IsSchedulePastDue ? " (timer was past due when last logged)" : string.Empty;
        var last = (aggregate.ScheduleTimestamp ?? aggregate.ScheduleLast)?.ToString("o") ?? "n/a";
        var next = aggregate.ScheduleNext.HasValue ? aggregate.ScheduleNext.Value.ToString("o") : "n/a";
        var updated = aggregate.ScheduleLastUpdated.HasValue ? aggregate.ScheduleLastUpdated.Value.ToString("o") : "n/a";
        return FormattableString.Invariant(
            $"Schedule {name}{pastDue}. Timer last: {last}, next: {next}, status last updated: {updated}.");
    }

    /// <summary>
    /// True when no run has left the initial <see cref="SyncJobProgressState.Created"/>-only phase (no started/done/exception/expired steps).
    /// Used to infer <see cref="SyncJobMonitorResponse.ExpectedRunAt"/> from timer CRON when schedule status blob data is missing.
    /// </summary>
    private static bool HasSyncJobNotStartedExecution(SyncJobMonitorAggregate aggregate)
    {
        if (aggregate?.Runs == null || aggregate.Runs.Count == 0)
        {
            return true;
        }

        foreach (var run in aggregate.Runs)
        {
            if (run.StepsByState == null)
            {
                continue;
            }

            foreach (var stateName in run.StepsByState.Keys)
            {
                if (string.Equals(stateName, nameof(SyncJobProgressState.Started), StringComparison.Ordinal) ||
                    string.Equals(stateName, nameof(SyncJobProgressState.Done), StringComparison.Ordinal) ||
                    string.Equals(stateName, nameof(SyncJobProgressState.Exception), StringComparison.Ordinal) ||
                    string.Equals(stateName, nameof(SyncJobProgressState.Expired), StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static IEnumerable<SyncJobMonitorProgressStepDto> BuildLatestProgressSteps(SyncJobMonitorAggregate aggregate)
    {
        if (aggregate == null || string.IsNullOrEmpty(aggregate.LatestSyncJobCorrelationId))
        {
            yield break;
        }

        var run = aggregate.Runs.FirstOrDefault(r =>
            string.Equals(r.SyncJobCorrelationId, aggregate.LatestSyncJobCorrelationId, StringComparison.Ordinal));
        if (run == null)
        {
            yield break;
        }

        foreach (var stateName in ProgressStateOrder)
        {
            if (run.StepsByState.TryGetValue(stateName, out var step))
            {
                yield return new SyncJobMonitorProgressStepDto(stateName, step.Occured, step.Message);
            }
        }
    }

    /// <summary>
    /// Source and target change-tracking versions per table only (no CHANGETABLE counts).
    /// </summary>
    private static async IAsyncEnumerable<SyncJobMonitorTableVersionRow> LoadMonitorTableVersionsAsync(
        SyncJob syncJob,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(syncJob);
        ArgumentNullException.ThrowIfNull(logger);

        await using SqlConnection
            sourceConn = new(syncJob.SourceDbConnection) { AccessToken = syncJob.SourceDbAccessToken },
            targetConn = new(syncJob.TargetDbConnection) { AccessToken = syncJob.TargetDbAccessToken };

        using IDisposable
            from = logger.BeginScope("{DataSource}.{Database}", sourceConn.DataSource, sourceConn.Database),
            to = logger.BeginScope("{DataSource}.{Database}", targetConn.DataSource, targetConn.Database);

        await sourceConn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await targetConn.OpenAsync(cancellationToken).ConfigureAwait(false);

        targetConn.EnsureSyncSchemaAndTableExists($"config/{syncJob.Id}/{syncJob.Area}/schema/tracking", logger);

        foreach (var table in syncJob.Tables ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();

            var columns = sourceConn.GetColumns(table.Source);
            var sourceVersion = sourceConn.GetSourceVersion(table.Source, globalChangeTracking: true, columns);
            var targetVersion = targetConn.GetTargetVersion(table.Target);
            var sourceVersionNumber = sourceVersion?.CurrentVersion ?? -1L;

            yield return new SyncJobMonitorTableVersionRow(
                table.Id,
                table.Source,
                sourceVersionNumber,
                targetVersion.CurrentVersion,
                targetVersion.Queried,
                targetVersion.Updated,
                table.Target
            );
        }
    }
}
