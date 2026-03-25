using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Models;
using SqlBulkSyncFunction.Models.Monitor;

namespace SqlBulkSyncFunction.Services;

/// <summary>
/// Drains schedule and sync-job progress queues in a fixed order, merges blobs into per-job aggregate blobs for cheap reads.
/// Aggregates are partitioned by schedule under <c>{area}/{jobId}/{schedule}.json</c> with optimistic concurrency (ETags).
/// </summary>
public sealed class SyncMonitoringAggregationService(
    ILogger<SyncMonitoringAggregationService> logger,
    QueueServiceClient queueService,
    BlobServiceClient blobServiceClient
    )
{
    private const int MaxRunsPerAggregate = 10;
    private const int MaxMessagesPerReceive = 32;
    private const int MaxConcurrencyRetries = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly QueueClient _logScheduleQueue = GetOrCreateQueue(queueService, Constants.Queues.LogScheduleQueue);
    private readonly Dictionary<SyncJobProgressState, QueueClient> _progressQueues = GetProgressQueues(queueService);
    private readonly BlobContainerClient _syncJobContainer = GetOrCreateContainer(blobServiceClient, Constants.Containers.SyncJob);
    private readonly BlobContainerClient _syncScheduleContainer = GetOrCreateContainer(blobServiceClient, Constants.Containers.SyncSchedule);
    private readonly BlobContainerClient _monitorContainer = GetOrCreateContainer(blobServiceClient, Constants.Containers.Monitor);

    /// <summary>
    /// Returns the persisted aggregate for <paramref name="area"/>, <paramref name="jobId"/>, and <paramref name="schedule"/>, or null if none.
    /// </summary>
    public async Task<SyncJobMonitorAggregate> GetAggregateAsync(string area, string jobId, string schedule, CancellationToken cancellationToken)
    {
        var path = GetMonitorBlobPath(area, jobId, schedule);
        var blob = _monitorContainer.GetBlobClient(path);
        if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var download = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        return download.Value.Content.ToObjectFromJson<SyncJobMonitorAggregate>(JsonOptions);
    }

    /// <summary>
    /// Processes all monitoring queues sequentially: log schedule first, then progress queues in enum order.
    /// </summary>
    public async Task ProcessAllQueuesAsync(CancellationToken cancellationToken)
    {
        await DrainQueueAsync(_logScheduleQueue, ProcessLogScheduleAsync, cancellationToken).ConfigureAwait(false);

        foreach (var state in Enum.GetValues<SyncJobProgressState>())
        {
            if (_progressQueues.TryGetValue(state, out var client))
            {
                await DrainQueueAsync(client, ProcessSyncJobProgressAsync, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DrainQueueAsync(
        QueueClient queue,
        Func<string, CancellationToken, Task> handleCorrelationId,
        CancellationToken cancellationToken
    )
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var response = await queue
                .ReceiveMessagesAsync(maxMessages: MaxMessagesPerReceive, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (response.Value.Length == 0)
            {
                return;
            }

            foreach (var message in response.Value)
            {
                var correlationId = message.Body.ToString();
                if (string.IsNullOrWhiteSpace(correlationId))
                {
                    await queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    await handleCorrelationId(correlationId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Monitoring aggregation failed for queue {Queue} message {CorrelationId}", queue.Name, correlationId);
                }

                await queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessLogScheduleAsync(string correlationId, CancellationToken cancellationToken)
    {
        var blob = _syncScheduleContainer.GetBlobClient($"{correlationId}.json");
        if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Schedule blob missing for {CorrelationId}", correlationId);
            return;
        }

        var download = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        var logSchedule = download.Value.Content.ToObjectFromJson<LogSchedule>(JsonOptions);
        if (logSchedule == null || string.IsNullOrWhiteSpace(logSchedule.Name))
        {
            return;
        }

        foreach (var syncJob in logSchedule.SyncJobs ?? [])
        {
            if (string.IsNullOrWhiteSpace(syncJob.Area) || string.IsNullOrWhiteSpace(syncJob.Id))
            {
                continue;
            }

            await SaveAggregateWithConcurrencyRetryAsync(
                syncJob.Area,
                syncJob.Id,
                logSchedule.Name,
                aggregate =>
                {
                    aggregate ??= new SyncJobMonitorAggregate(syncJob.Area, syncJob.Id, logSchedule.Name);
                    aggregate.ScheduleName = logSchedule.Name;
                    aggregate.ScheduleCorrelationId = logSchedule.CorrelationId;
                    aggregate.ScheduleTimestamp = logSchedule.Timestamp;
                    aggregate.ScheduleExpires = logSchedule.Expires;
                    aggregate.ScheduleLast = logSchedule.Last;
                    aggregate.ScheduleNext = logSchedule.Next;
                    aggregate.ScheduleLastUpdated = logSchedule.LastUpdated;
                    aggregate.IsSchedulePastDue = logSchedule.IsPastDue;
                    aggregate.AggregatedAt = DateTimeOffset.UtcNow;
                    return aggregate;
                },
                cancellationToken
            ).ConfigureAwait(false);
        }
    }

    private async Task ProcessSyncJobProgressAsync(string correlationId, CancellationToken cancellationToken)
    {
        var blob = _syncJobContainer.GetBlobClient($"{correlationId}.json");
        if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Sync job progress blob missing for {CorrelationId}", correlationId);
            return;
        }

        var download = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        var progress = download.Value.Content.ToObjectFromJson<SyncJobProgress>(JsonOptions);
        if (progress == null ||
            string.IsNullOrWhiteSpace(progress.Area) ||
            string.IsNullOrWhiteSpace(progress.ConfigurationId) ||
            string.IsNullOrWhiteSpace(progress.Schedule))
        {
            return;
        }

        var scheduleName = progress.Schedule;

        await SaveAggregateWithConcurrencyRetryAsync(
            progress.Area,
            progress.ConfigurationId,
            scheduleName,
            aggregate =>
            {
                aggregate ??= new SyncJobMonitorAggregate(progress.Area, progress.ConfigurationId, scheduleName);

                var runKey = progress.SyncJobCorrelationId;
                var run = aggregate.Runs.FirstOrDefault(r => string.Equals(r.SyncJobCorrelationId, runKey, StringComparison.Ordinal));
                if (run == null)
                {
                    run = new SyncJobRunAggregate(runKey);
                    aggregate.Runs.Add(run);
                }

                var stateName = progress.State.ToString("F");
                run.StepsByState[stateName] = new SyncJobProgressStepAggregate(progress.Occured, progress.Message);
                run.LastActivity = run.StepsByState.Values.Max(s => s.Occured);

                PruneRuns(aggregate);

                if (!aggregate.LastProgressOccured.HasValue || progress.Occured >= aggregate.LastProgressOccured.Value)
                {
                    aggregate.LastProgressOccured = progress.Occured;
                    aggregate.LatestSyncJobCorrelationId = runKey;
                }

                aggregate.AggregatedAt = DateTimeOffset.UtcNow;
                return aggregate;
            },
            cancellationToken
        ).ConfigureAwait(false);
    }

    private static void PruneRuns(SyncJobMonitorAggregate aggregate)
    {
        if (aggregate.Runs.Count <= MaxRunsPerAggregate)
        {
            return;
        }

        var ordered = aggregate.Runs
            .OrderByDescending(r => r.LastActivity)
            .Take(MaxRunsPerAggregate)
            .ToList();
        aggregate.Runs.Clear();
        aggregate.Runs.AddRange(ordered);
    }

    /// <summary>
    /// Loads, applies <paramref name="merge"/>, and saves with ETag preconditions; retries on write conflicts.
    /// </summary>
    private async Task SaveAggregateWithConcurrencyRetryAsync(
        string area,
        string jobId,
        string scheduleName,
        Func<SyncJobMonitorAggregate, SyncJobMonitorAggregate> merge,
        CancellationToken cancellationToken
    )
    {
        var path = GetMonitorBlobPath(area, jobId, scheduleName);
        var blob = _monitorContainer.GetBlobClient(path);

        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            ETag? etag = null;
            SyncJobMonitorAggregate existing = null;
            if (await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                var dl = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
                etag = dl.Value.Details.ETag;
                existing = dl.Value.Content.ToObjectFromJson<SyncJobMonitorAggregate>(JsonOptions);
            }

            var merged = merge(existing);
            if (merged == null)
            {
                return;
            }

            try
            {
                await UploadMonitorAggregateAsync(blob, merged, etag, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (RequestFailedException ex) when (IsConcurrencyConflict(ex) && attempt < MaxConcurrencyRetries - 1)
            {
                logger.LogDebug(
                    ex,
                    "Monitor aggregate concurrency conflict for {Path}, retry {Attempt}",
                    path,
                    attempt + 1);
            }
        }

        throw new InvalidOperationException(
            FormattableString.Invariant($"Could not save monitor aggregate after {MaxConcurrencyRetries} attempts: {path}"));
    }

    private static bool IsConcurrencyConflict(RequestFailedException ex)
        => ex.Status == 412 || ex.Status == 409;

    private static async Task UploadMonitorAggregateAsync(
        BlobClient blob,
        SyncJobMonitorAggregate aggregate,
        ETag? etagFromDownload,
        CancellationToken cancellationToken
    )
    {
        var data = BinaryData.FromObjectAsJson(aggregate, JsonOptions);
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = Constants.BlobContentTypes.Json,
            },
        };

        if (etagFromDownload.HasValue)
        {
            options.Conditions = new BlobRequestConditions { IfMatch = etagFromDownload.Value };
        }
        else
        {
            options.Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All };
        }

        await blob.UploadAsync(data, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Virtual path under the monitor container: <c>{area}/{jobId}/{schedule}.json</c> (segments sanitized).
    /// </summary>
    internal static string GetMonitorBlobPath(string area, string jobId, string schedule)
        => FormattableString.Invariant(
            $"{SanitizeBlobPathSegment(area)}/{SanitizeBlobPathSegment(jobId)}/{SanitizeBlobPathSegment(schedule)}.json");

    /// <summary>
    /// Replaces characters that are unsafe or ambiguous in blob path segments.
    /// </summary>
    internal static string SanitizeBlobPathSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return "_";
        }

        var s = segment.Trim();
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '/' || c == '\\' || c == '#' || c == '?' || char.IsControl(c))
            {
                _ = sb.Append('_');
            }
            else
            {
                _ = sb.Append(c);
            }
        }

        return sb.Length > 0 ? sb.ToString() : "_";
    }

    private static QueueClient GetOrCreateQueue(QueueServiceClient queueService, string queueName)
    {
        var q = queueService.GetQueueClient(queueName);
        _ = q.CreateIfNotExists();
        return q;
    }

    private static BlobContainerClient GetOrCreateContainer(BlobServiceClient blobServiceClient, string name)
    {
        var c = blobServiceClient.GetBlobContainerClient(name);
        _ = c.CreateIfNotExists();
        return c;
    }

    private static Dictionary<SyncJobProgressState, QueueClient> GetProgressQueues(QueueServiceClient queueService)
    {
        var map = new Dictionary<SyncJobProgressState, QueueClient>();
        foreach (var state in Enum.GetValues<SyncJobProgressState>())
        {
            var queueName = FormattableString.Invariant($"{Constants.Queues.SyncJobProgressQueue}-{state.ToString("F").ToLowerInvariant()}");
            map[state] = GetOrCreateQueue(queueService, queueName);
        }

        return map;
    }
}
