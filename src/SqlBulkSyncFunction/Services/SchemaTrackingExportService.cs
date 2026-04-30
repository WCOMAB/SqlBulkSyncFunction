using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Models.Schema.Export;

namespace SqlBulkSyncFunction.Services;

#nullable enable

/// <summary>
/// Coordinates schema tracking export jobs (blobs, queues, table state, and segment processing).
/// </summary>
public sealed class SchemaTrackingExportService(
    ILogger<SchemaTrackingExportService> logger,
    IOptions<SyncJobsConfig> syncJobsConfig,
    ITokenCacheService tokenCacheService,
    BlobServiceClient blobServiceClient,
    QueueServiceClient queueServiceClient,
    TableServiceClient tableServiceClient
    )
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly BlobContainerClient _exportContainer = GetOrCreateBlobContainer(blobServiceClient, Constants.Containers.Export);
    private readonly TableClient _exportJobsTable = GetOrCreateTable(tableServiceClient, Constants.Tables.ExportJobs);

    /// <summary>
    /// Validates configuration, persists request and job blobs, creates the table row, and enqueues the main export queue.
    /// </summary>
    public async Task<ExportJobCreateResult> TryCreateExportJobAsync(
        string area,
        string jobId,
        string tableId,
        SchemaTrackingExportRequestBody request,
        CancellationToken cancellationToken
        )
    {
        if (string.IsNullOrWhiteSpace(request.Author) ||
            string.IsNullOrWhiteSpace(request.ReferenceId) ||
            string.IsNullOrWhiteSpace(request.Purpose))
        {
            return new ExportJobCreateResult(ExportJobCreateResultCode.ValidationFailed, null);
        }

        if (!TryResolveJobTable(area, jobId, tableId))
        {
            return new ExportJobCreateResult(ExportJobCreateResultCode.NotFound, null);
        }

        var utcNow = DateTimeOffset.UtcNow;
        var jobGuid = Guid.CreateVersion7();
        var exportJobId = jobGuid.ToString("n");
        var correlationId = FormattableString.Invariant(
            $"{utcNow.Year:0000}/{utcNow.Month:00}/{utcNow.Day:00}/{utcNow.Hour:00}/{utcNow.Minute:00}/{exportJobId}");

        var job = new SchemaTrackingExportJob(
            Area: area,
            Id: jobId,
            TableId: tableId,
            CorrelationId: correlationId,
            ExportJobId: exportJobId,
            ReferenceId: request.ReferenceId,
            Author: request.Author,
            Purpose: request.Purpose,
            Created: utcNow
        );

        var requestBlob = _exportContainer.GetBlobClient($"{correlationId}/request.json");
        var jobBlob = _exportContainer.GetBlobClient($"{correlationId}/job.json");

        _ = await requestBlob.UploadAsync(
            BinaryData.FromObjectAsJson(request, JsonOptions),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = Constants.BlobContentTypes.Json }
            },
            cancellationToken
        ).ConfigureAwait(false);

        _ = await jobBlob.UploadAsync(
            BinaryData.FromObjectAsJson(job, JsonOptions),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = Constants.BlobContentTypes.Json }
            },
            cancellationToken
        ).ConfigureAwait(false);

        var entity = new ExportJobTableEntity
        {
            PartitionKey = SchemaTrackingExportTableKeys.GetPartitionKey(area, jobId, tableId),
            RowKey = exportJobId,
            CorrelationId = correlationId,
            Area = area,
            JobId = jobId,
            TableId = tableId,
            ReferenceId = request.ReferenceId,
            Status = nameof(SchemaTrackingExportJobStatus.Pending),
            CreatedUtc = utcNow,
            UpdatedDone = false,
            InsertedDone = false,
            DeletedDone = false
        };

        _ = await _exportJobsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);

        var mainQueue = GetOrCreateQueue(Constants.Queues.ExportJob);
        _ = await mainQueue.SendMessageAsync(correlationId, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ExportJobCreateResult(ExportJobCreateResultCode.Created, job);
    }

    /// <summary>
    /// Loads job and table state for a single correlation id under the given route context (same DTO as <see cref="ListExportJobsAsync"/>).
    /// </summary>
    public async Task<SchemaTrackingExportJobListItem?> TryGetExportStatusAsync(
        string area,
        string jobId,
        string tableId,
        string correlationId,
        CancellationToken cancellationToken
        )
    {
        if (!TryResolveJobTable(area, jobId, tableId))
        {
            return null;
        }

        var normalizedCorrelation = NormalizeCorrelationId(correlationId);
        if (string.IsNullOrEmpty(normalizedCorrelation))
        {
            return null;
        }

        var job = await ReadJobBlobAsync(normalizedCorrelation, cancellationToken).ConfigureAwait(false);
        if (job == null)
        {
            return null;
        }

        if (!PartitionMatchesRoute(job, area, jobId, tableId))
        {
            return null;
        }

        var partitionKey = SchemaTrackingExportTableKeys.GetPartitionKey(area, jobId, tableId);
        var rowKey = job.ExportJobId;

        ExportJobTableEntity? entity;
        try
        {
            var response = await _exportJobsTable
                .GetEntityAsync<ExportJobTableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            entity = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            entity = null;
        }

        var resultBlob = _exportContainer.GetBlobClient($"{normalizedCorrelation}/response/result.json");
        SchemaTrackingExportJobResult? result = null;
        if (await resultBlob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                var download = await resultBlob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
                result = download.Value.Content.ToObjectFromJson<SchemaTrackingExportJobResult>(JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize result.json for {CorrelationId}", normalizedCorrelation);
            }
        }

        var listResult = SchemaTrackingExportJobListItemResult.FromJobResult(result);

        if (entity != null)
        {
            return ExportJobTableMapper.ToListItem(entity, listResult);
        }

        return new SchemaTrackingExportJobListItem(
            CorrelationId: job.CorrelationId,
            ExportJobId: job.ExportJobId,
            Status: ExportJobTableMapper.ParseStatus(null),
            ReferenceId: job.ReferenceId,
            Created: job.Created,
            Completed: null,
            UpdatedDone: false,
            InsertedDone: false,
            DeletedDone: false,
            Error: null,
            Result: listResult
        );
    }

    /// <summary>
    /// Lists export jobs for the partition derived from <paramref name="area"/>, <paramref name="jobId"/>, and <paramref name="tableId"/>.
    /// </summary>
    public async Task<IReadOnlyList<SchemaTrackingExportJobListItem>> ListExportJobsAsync(
        string area,
        string jobId,
        string tableId,
        CancellationToken cancellationToken
        )
    {
        if (!TryResolveJobTable(area, jobId, tableId))
        {
            return [];
        }

        var partitionKey = SchemaTrackingExportTableKeys.GetPartitionKey(area, jobId, tableId);
        var filter = FormattableString.Invariant($"PartitionKey eq '{EscapeODataString(partitionKey)}'");
        return await _exportJobsTable
            .QueryAsync<ExportJobTableEntity>(
                filter,
                cancellationToken: cancellationToken
            )
            .Select(static e => ExportJobTableMapper.ToListItem(e))
            .OrderByDescending(static x => x.Created)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches a main-queue message to the three segment queues.
    /// </summary>
    public async Task DispatchExportJobAsync(string correlationId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeCorrelationId(correlationId);
        var job = await ReadJobBlobAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (job == null)
        {
            logger.LogWarning("Dispatch skipped: missing job.json for {CorrelationId}", normalized);
            return;
        }

        var partitionKey = SchemaTrackingExportTableKeys.GetPartitionKey(job.Area, job.Id, job.TableId);
        var rowKey = job.ExportJobId;
        await PatchEntityAsync(
            partitionKey,
            rowKey,
            static e => e.Status = nameof(SchemaTrackingExportJobStatus.Running),
            cancellationToken
        ).ConfigureAwait(false);

        var qUpdated = GetOrCreateQueue(Constants.Queues.ExportJobUpdated);
        var qInserted = GetOrCreateQueue(Constants.Queues.ExportJobInserted);
        var qDeleted = GetOrCreateQueue(Constants.Queues.ExportJobDeleted);

        // Do not pass visibilityTimeout on enqueue: non-zero values hide the message from Dequeue/Peek until the timeout elapses (scheduled/delayed work), not a processing lease.
        _ = await qUpdated.SendMessageAsync(normalized, cancellationToken: cancellationToken).ConfigureAwait(false);
        _ = await qInserted.SendMessageAsync(normalized, cancellationToken: cancellationToken).ConfigureAwait(false);
        _ = await qDeleted.SendMessageAsync(normalized, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds one segment ZIP from SQL change tracking and notifies the corresponding done queue.
    /// </summary>
    public async Task ProcessExportSegmentAsync(
        string correlationId,
        SchemaTrackingExportSegment segment,
        CancellationToken cancellationToken
        )
    {
        var normalized = NormalizeCorrelationId(correlationId);
        var job = await ReadJobBlobAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (job == null)
        {
            logger.LogWarning("Segment worker: missing job for {CorrelationId}", normalized);
            return;
        }

        if (!syncJobsConfig.Value.Jobs.TryGetValue(job.Id, out var jobConfig) ||
            !string.Equals(jobConfig.Area, job.Area, StringComparison.OrdinalIgnoreCase))
        {
            await MarkJobFailedAsync(job, "Job configuration not found or area mismatch.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var syncJob = jobConfig.ToSyncJob(
            scheduleCorrelationId: null,
            tokenCache: await tokenCacheService.GetTokenCache(jobConfig).ConfigureAwait(false),
            timestamp: DateTimeOffset.UtcNow,
            expires: DateTimeOffset.UtcNow.AddMinutes(4),
            id: job.Id,
            schedule: nameof(jobConfig.Manual),
            seed: false
        );

        var table = (syncJob.Tables ?? []).FirstOrDefault(
            t => string.Equals(t.Id, job.TableId, StringComparison.OrdinalIgnoreCase)
        );
        if (table == null)
        {
            await MarkJobFailedAsync(job, "Table mapping not found.", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var sourceConn = new SqlConnection(syncJob.SourceDbConnection) { AccessToken = syncJob.SourceDbAccessToken };
            await using var targetConn = new SqlConnection(syncJob.TargetDbConnection) { AccessToken = syncJob.TargetDbAccessToken };
            await sourceConn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await targetConn.OpenAsync(cancellationToken).ConfigureAwait(false);

            targetConn.EnsureSyncSchemaAndTableExists(
                FormattableString.Invariant($"config/{job.Id}/{job.Area}/schema/tracking/{job.TableId}"),
                logger
            );

            var columns = sourceConn.GetColumns(table.Source);
            var sourceVersion = sourceConn.GetSourceVersion(table.Source, globalChangeTracking: true, columns);
            var targetVersion = targetConn.GetTargetVersion(table.Target);
            var fromVersion = targetVersion.CurrentVersion < 0 ? 0L : targetVersion.CurrentVersion;

            var sql = SqlStatementExtensions.GetChangeTrackingExportSegmentSelectStatement(table.Source, columns, segment);

            await using var cmd = new SqlCommand(sql, sourceConn)
            {
                CommandTimeout = 3600
            };
            _ = cmd.Parameters.Add(new SqlParameter("@FromVersion", SqlDbType.BigInt) { Value = fromVersion });

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken)
                .ConfigureAwait(false);

            var (zipPath, jsonPath) = GetZipRelativePath(segment);
            var zipBlob = _exportContainer.GetBlobClient($"{normalized}/{zipPath}");
            await using var uploadStream = await zipBlob.OpenWriteAsync(
                    true,
                    new BlobOpenWriteOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = Constants.BlobContentTypes.Zip }
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);

            await SchemaTrackingExportStreamingZip.WriteReaderToZipAsync(reader, uploadStream, jsonPath, cancellationToken)
                .ConfigureAwait(false);

            var doneQueueName = GetDoneQueueName(segment);
            var doneQueue = GetOrCreateQueue(doneQueueName);
            _ = await doneQueue.SendMessageAsync(normalized, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Export segment {Segment} failed for {CorrelationId}", segment, normalized);
            await MarkJobFailedAsync(job, ex.Message, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Records segment completion and finalizes the job when all segments are done.
    /// </summary>
    public async Task OnExportSegmentDoneAsync(
        string correlationId,
        SchemaTrackingExportSegment segment,
        CancellationToken cancellationToken
        )
    {
        var normalized = NormalizeCorrelationId(correlationId);
        var job = await ReadJobBlobAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (job == null)
        {
            logger.LogWarning(
                "Segment done handler: missing job.json for {CorrelationId} (check queue message body / encoding vs blob path).",
                normalized
            );
            return;
        }

        var partitionKey = SchemaTrackingExportTableKeys.GetPartitionKey(job.Area, job.Id, job.TableId);
        var rowKey = job.ExportJobId;

        for (var attempt = 0; attempt < 12; attempt++)
        {
            Response<ExportJobTableEntity> response;
            try
            {
                response = await _exportJobsTable
                    .GetEntityAsync<ExportJobTableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                logger.LogWarning("Finalize: missing table entity for {CorrelationId}", normalized);
                return;
            }

            var entity = response.Value;
            if (IsLegDone(entity, segment))
            {
                var alreadyRefreshed = await _exportJobsTable
                    .GetEntityAsync<ExportJobTableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await TryFinalizeIfCompleteAsync(job, alreadyRefreshed.Value, cancellationToken).ConfigureAwait(false);
                return;
            }

            SetLegDone(entity, segment, true);

            try
            {
                _ = await _exportJobsTable.UpdateEntityAsync(
                    entity,
                    response.Value.ETag,
                    TableUpdateMode.Merge,
                    cancellationToken
                ).ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                continue;
            }

            var refreshed = await _exportJobsTable
                .GetEntityAsync<ExportJobTableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await TryFinalizeIfCompleteAsync(job, refreshed.Value, cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.LogWarning("Finalize: exhausted optimistic retries for {CorrelationId}", normalized);
    }

    private async Task TryFinalizeIfCompleteAsync(
        SchemaTrackingExportJob job,
        ExportJobTableEntity entity,
        CancellationToken cancellationToken
        )
    {
        if (!entity.UpdatedDone || !entity.InsertedDone || !entity.DeletedDone)
        {
            return;
        }

        var correlationId = NormalizeCorrelationId(job.CorrelationId);
        var resultBlob = _exportContainer.GetBlobClient($"{correlationId}/response/result.json");
        if (await resultBlob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            await MarkTableCompletedAsync(job, cancellationToken).ConfigureAwait(false);
            return;
        }

        var updatedZip = _exportContainer.GetBlobClient($"{correlationId}/response/updated.zip");
        var insertedZip = _exportContainer.GetBlobClient($"{correlationId}/response/inserted.zip");
        var deletedZip = _exportContainer.GetBlobClient($"{correlationId}/response/deleted.zip");

        if (!await updatedZip.ExistsAsync(cancellationToken).ConfigureAwait(false) ||
            !await insertedZip.ExistsAsync(cancellationToken).ConfigureAwait(false) ||
            !await deletedZip.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Finalize: missing one or more ZIP blobs for {CorrelationId}", correlationId);
            return;
        }

        var sasExpires = DateTimeOffset.UtcNow.AddDays(7);
        var updatedUri = GenerateReadSasUri(updatedZip, sasExpires);
        var insertedUri = GenerateReadSasUri(insertedZip, sasExpires);
        var deletedUri = GenerateReadSasUri(deletedZip, sasExpires);

        var completed = DateTimeOffset.UtcNow;
        var result = new SchemaTrackingExportJobResult(
            Area: job.Area,
            Id: job.Id,
            TableId: job.TableId,
            CorrelationId: job.CorrelationId,
            ExportJobId: job.ExportJobId,
            ReferenceId: job.ReferenceId,
            Author: job.Author,
            Purpose: job.Purpose,
            Created: job.Created,
            Completed: completed,
            SasExpires: sasExpires,
            UpdatedZipSasUri: updatedUri,
            InsertedZipSasUri: insertedUri,
            DeletedZipSasUri: deletedUri
        );

        try
        {
            _ = await resultBlob.UploadAsync(
                BinaryData.FromObjectAsJson(result, JsonOptions),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = Constants.BlobContentTypes.Json },
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                },
                cancellationToken
            ).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            logger.LogInformation("result.json already created for {CorrelationId}", correlationId);
        }

        await MarkTableCompletedAsync(job, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkTableCompletedAsync(SchemaTrackingExportJob job, CancellationToken cancellationToken)
    {
        var partitionKey = SchemaTrackingExportTableKeys.GetPartitionKey(job.Area, job.Id, job.TableId);
        var rowKey = job.ExportJobId;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            Response<ExportJobTableEntity> response;
            try
            {
                response = await _exportJobsTable
                    .GetEntityAsync<ExportJobTableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return;
            }

            var entity = response.Value;
            if (string.Equals(entity.Status, nameof(SchemaTrackingExportJobStatus.Completed), StringComparison.Ordinal))
            {
                return;
            }

            entity.Status = nameof(SchemaTrackingExportJobStatus.Completed);
            entity.CompletedUtc = DateTimeOffset.UtcNow;
            entity.UpdatedDone = true;
            entity.InsertedDone = true;
            entity.DeletedDone = true;

            try
            {
                _ = await _exportJobsTable.UpdateEntityAsync(
                    entity,
                    response.Value.ETag,
                    TableUpdateMode.Merge,
                    cancellationToken
                ).ConfigureAwait(false);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                continue;
            }
        }
    }

    private async Task MarkJobFailedAsync(SchemaTrackingExportJob job, string error, CancellationToken cancellationToken)
    {
        var partitionKey = SchemaTrackingExportTableKeys.GetPartitionKey(job.Area, job.Id, job.TableId);
        var rowKey = job.ExportJobId;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                var response = await _exportJobsTable
                    .GetEntityAsync<ExportJobTableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var entity = response.Value;
                entity.Status = nameof(SchemaTrackingExportJobStatus.Failed);
                entity.Error = error;
                _ = await _exportJobsTable.UpdateEntityAsync(
                    entity,
                    response.Value.ETag,
                    TableUpdateMode.Merge,
                    cancellationToken
                ).ConfigureAwait(false);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                continue;
            }
        }
    }

    private async Task PatchEntityAsync(
        string partitionKey,
        string rowKey,
        Action<ExportJobTableEntity> patch,
        CancellationToken cancellationToken
        )
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                var response = await _exportJobsTable
                    .GetEntityAsync<ExportJobTableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var entity = response.Value;
                patch(entity);
                _ = await _exportJobsTable.UpdateEntityAsync(
                    entity,
                    response.Value.ETag,
                    TableUpdateMode.Merge,
                    cancellationToken
                ).ConfigureAwait(false);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                continue;
            }
        }
    }

    private static Uri GenerateReadSasUri(BlobClient blob, DateTimeOffset expiresOn)
    {
        var sas = new BlobSasBuilder
        {
            Resource = "b",
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = expiresOn
        };
        sas.SetPermissions(BlobSasPermissions.Read);
        return blob.GenerateSasUri(sas);
    }

    private static bool IsLegDone(ExportJobTableEntity entity, SchemaTrackingExportSegment segment)
        => segment switch
        {
            SchemaTrackingExportSegment.Updated => entity.UpdatedDone,
            SchemaTrackingExportSegment.Inserted => entity.InsertedDone,
            SchemaTrackingExportSegment.Deleted => entity.DeletedDone,
            _ => false
        };

    private static void SetLegDone(ExportJobTableEntity entity, SchemaTrackingExportSegment segment, bool value)
    {
        switch (segment)
        {
            case SchemaTrackingExportSegment.Updated:
                entity.UpdatedDone = value;
                break;
            case SchemaTrackingExportSegment.Inserted:
                entity.InsertedDone = value;
                break;
            case SchemaTrackingExportSegment.Deleted:
                entity.DeletedDone = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(segment));
        }
    }

    private static string GetDoneQueueName(SchemaTrackingExportSegment segment)
        => segment switch
        {
            SchemaTrackingExportSegment.Updated => Constants.Queues.ExportJobUpdatedDone,
            SchemaTrackingExportSegment.Inserted => Constants.Queues.ExportJobInsertedDone,
            SchemaTrackingExportSegment.Deleted => Constants.Queues.ExportJobDeletedDone,
            _ => throw new ArgumentOutOfRangeException(nameof(segment))
        };

    /// <summary>
    /// Blob path under the correlation prefix for the segment ZIP, and the single JSON entry name inside that ZIP.
    /// </summary>
    /// <param name="segment">Export segment.</param>
    /// <returns><c>ZipPath</c> matches finalize/SAS blob names (<c>response/*.zip</c>); <c>JsonPath</c> is the inner entry file name.</returns>
    private static (string ZipPath, string JsonPath) GetZipRelativePath(SchemaTrackingExportSegment segment)
        => segment switch
        {
            SchemaTrackingExportSegment.Updated => ("response/updated.zip", "updated.json"),
            SchemaTrackingExportSegment.Inserted => ("response/inserted.zip", "inserted.json"),
            SchemaTrackingExportSegment.Deleted => ("response/deleted.zip", "deleted.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(segment))
        };

    private async Task<SchemaTrackingExportJob?> ReadJobBlobAsync(string correlationId, CancellationToken cancellationToken)
    {
        var blob = _exportContainer.GetBlobClient($"{correlationId}/job.json");
        if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var content = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
        return content.Value.Content.ToObjectFromJson<SchemaTrackingExportJob>(JsonOptions);
    }

    private bool TryResolveJobTable(string area, string jobId, string tableId)
    {
        var jobs = syncJobsConfig.Value.Jobs;
        if (string.IsNullOrWhiteSpace(area) ||
            string.IsNullOrWhiteSpace(jobId) ||
            string.IsNullOrWhiteSpace(tableId) ||
            jobs == null ||
            !jobs.TryGetValue(jobId, out var jc) ||
            jc == null ||
            !string.Equals(jc.Area, area, StringComparison.OrdinalIgnoreCase) ||
            jc.Tables == null ||
            !jc.Tables.TryGetValue(tableId, out var sourceTableName) ||
            string.IsNullOrWhiteSpace(sourceTableName))
        {
            return false;
        }

        return true;
    }

    private static bool PartitionMatchesRoute(SchemaTrackingExportJob job, string area, string jobId, string tableId)
        => string.Equals(job.Area, area, StringComparison.OrdinalIgnoreCase)
            && string.Equals(job.Id, jobId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(job.TableId, tableId, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCorrelationId(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return string.Empty;
        }

        var s = correlationId.Trim().Replace('\\', '/').Trim('/');
        if (s.Contains("..", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return s;
    }

    private static string EscapeODataString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static BlobContainerClient GetOrCreateBlobContainer(BlobServiceClient client, string name)
    {
        var c = client.GetBlobContainerClient(name);
        _ = c.CreateIfNotExists(PublicAccessType.None);
        return c;
    }

    private static TableClient GetOrCreateTable(TableServiceClient client, string tableName)
    {
        var t = client.GetTableClient(tableName);
        _ = t.CreateIfNotExists();
        return t;
    }

    private QueueClient GetOrCreateQueue(string queueName)
    {
        var q = queueServiceClient.GetQueueClient(queueName);
        _ = q.CreateIfNotExists();
        return q;
    }
}
