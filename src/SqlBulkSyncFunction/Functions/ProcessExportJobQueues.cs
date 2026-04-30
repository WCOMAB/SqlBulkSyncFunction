using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Models.Schema.Export;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions;

/// <summary>
/// Queue-triggered handlers for schema tracking export orchestration (dispatch, segment ZIPs, finalize).
/// </summary>
public sealed class ProcessExportJobQueues(
    ILogger<ProcessExportJobQueues> logger,
    QueueServiceClient queueServiceClient,
    SchemaTrackingExportService schemaTrackingExportService
    )
{
    /// <summary>
    /// Fans out a new export job to the three segment queues.
    /// </summary>
    [Function(nameof(ProcessExportJobQueues) + nameof(DispatchExportJob))]
    public async Task DispatchExportJob(
        [QueueTrigger(Constants.Queues.ExportJob)] string correlationId,
        CancellationToken cancellationToken
        )
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);

        using (logger.BeginScope("CorrelationId={CorrelationId}", correlationId))
        {
            await schemaTrackingExportService
                .DispatchExportJobAsync(correlationId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the updated-rows ZIP for an export job.
    /// </summary>
    [Function(nameof(ProcessExportJobQueues) + nameof(ProcessExportUpdated))]
    public Task ProcessExportUpdated(
        [QueueTrigger(Constants.Queues.ExportJobUpdated)] string correlationId,
        CancellationToken cancellationToken
        ) => ProcessSegmentAsync(correlationId, SchemaTrackingExportSegment.Updated, cancellationToken);

    /// <summary>
    /// Builds the inserted-rows ZIP for an export job.
    /// </summary>
    [Function(nameof(ProcessExportJobQueues) + nameof(ProcessExportInserted))]
    public Task ProcessExportInserted(
        [QueueTrigger(Constants.Queues.ExportJobInserted)] string correlationId,
        CancellationToken cancellationToken
        ) => ProcessSegmentAsync(correlationId, SchemaTrackingExportSegment.Inserted, cancellationToken);

    /// <summary>
    /// Builds the deleted-rows ZIP (primary keys) for an export job.
    /// </summary>
    [Function(nameof(ProcessExportJobQueues) + nameof(ProcessExportDeleted))]
    public Task ProcessExportDeleted(
        [QueueTrigger(Constants.Queues.ExportJobDeleted)] string correlationId,
        CancellationToken cancellationToken
        ) => ProcessSegmentAsync(correlationId, SchemaTrackingExportSegment.Deleted, cancellationToken);

    /// <summary>
    /// Records completion of the updated segment and finalizes the job when all segments are done.
    /// </summary>
    [Function(nameof(ProcessExportJobQueues) + nameof(OnExportUpdatedDone))]
    public Task OnExportUpdatedDone(
        [QueueTrigger(Constants.Queues.ExportJobUpdatedDone)] string correlationId,
        CancellationToken cancellationToken
        ) => OnSegmentDoneAsync(correlationId, SchemaTrackingExportSegment.Updated, cancellationToken);

    /// <summary>
    /// Records completion of the inserted segment and finalizes the job when all segments are done.
    /// </summary>
    [Function(nameof(ProcessExportJobQueues) + nameof(OnExportInsertedDone))]
    public Task OnExportInsertedDone(
        [QueueTrigger(Constants.Queues.ExportJobInsertedDone)] string correlationId,
        CancellationToken cancellationToken
        ) => OnSegmentDoneAsync(correlationId, SchemaTrackingExportSegment.Inserted, cancellationToken);

    /// <summary>
    /// Records completion of the deleted segment and finalizes the job when all segments are done.
    /// </summary>
    [Function(nameof(ProcessExportJobQueues) + nameof(OnExportDeletedDone))]
    public Task OnExportDeletedDone(
        [QueueTrigger(Constants.Queues.ExportJobDeletedDone)] string correlationId,
        CancellationToken cancellationToken
        ) => OnSegmentDoneAsync(correlationId, SchemaTrackingExportSegment.Deleted, cancellationToken);

    private async Task ProcessSegmentAsync(
        string correlationId,
        SchemaTrackingExportSegment segment,
        CancellationToken cancellationToken
        )
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);

        using (logger.BeginScope("CorrelationId={CorrelationId}, Segment={Segment}", correlationId, segment))
        {
            try
            {
                await schemaTrackingExportService
                    .ProcessExportSegmentAsync(correlationId, segment, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Export segment {Segment} failed for {CorrelationId}; sending message to segment error queue.",
                    segment,
                    correlationId
                );
                await EnqueueSegmentErrorAsync(correlationId, segment, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task OnSegmentDoneAsync(
        string correlationId,
        SchemaTrackingExportSegment segment,
        CancellationToken cancellationToken
        )
    {
        ArgumentException.ThrowIfNullOrEmpty(correlationId);

        using (logger.BeginScope("CorrelationId={CorrelationId}, Segment={Segment}", correlationId, segment))
        {
            await schemaTrackingExportService
                .OnExportSegmentDoneAsync(correlationId, segment, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task EnqueueSegmentErrorAsync(
        string correlationId,
        SchemaTrackingExportSegment segment,
        CancellationToken cancellationToken
        )
    {
        var queueName = segment switch
        {
            SchemaTrackingExportSegment.Updated => Constants.Queues.ExportJobUpdatedError,
            SchemaTrackingExportSegment.Inserted => Constants.Queues.ExportJobInsertedError,
            SchemaTrackingExportSegment.Deleted => Constants.Queues.ExportJobDeletedError,
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, null)
        };

        var queue = queueServiceClient.GetQueueClient(queueName);
        _ = await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        _ = await queue.SendMessageAsync(correlationId, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
