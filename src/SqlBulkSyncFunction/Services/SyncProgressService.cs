using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using SqlBulkSyncFunction.Models;

namespace SqlBulkSyncFunction.Functions;

public class SyncProgressService(
    QueueServiceClient queueService,
    BlobServiceClient blobServiceClient
    )
{
    private readonly QueueClient _logScheduleQueueClient = GetQueueClient(queueService, Constants.Queues.LogScheduleQueue);
    private readonly ImmutableDictionary<SyncJobProgressState, QueueClient> _queueClients = GetQueueClients(queueService);
    private readonly BlobContainerClient _syncJobBlobContainerClient = GetBlobContainerClient(blobServiceClient, Constants.Containers.SyncJob);
    private readonly BlobContainerClient _syncScheduleBlobContainerClient = GetBlobContainerClient(blobServiceClient, Constants.Containers.SyncSchedule);

    private static BlobContainerClient GetBlobContainerClient(BlobServiceClient blobServiceClient, string blobContainerName)
    {
        var blobContainerClient = blobServiceClient.GetBlobContainerClient(blobContainerName);
        if (!blobContainerClient.Exists())
        {
            _ = blobContainerClient.CreateIfNotExists();
        }
        return blobContainerClient;
    }

    private static ImmutableDictionary<SyncJobProgressState, QueueClient> GetQueueClients(QueueServiceClient queueService)
    {
        var queueClients = ImmutableDictionary.ToImmutableDictionary(
            source: Enum.GetValues<SyncJobProgressState>(),
            keySelector: state => state,
            elementSelector: state =>
            {
                var queueName = $"{Constants.Queues.SyncJobProgressQueue}-{state.ToString("F").ToLower()}";
                return GetQueueClient(queueService, queueName);
            }
        );
        return queueClients;
    }

    private static QueueClient GetQueueClient(QueueServiceClient queueService, string queueName)
    {
        var queueClient = queueService.GetQueueClient(queueName);
        if (!queueClient.Exists())
        {
            _ = queueClient.CreateIfNotExists();
        }
        return queueClient;
    }

    public async Task Report(SyncJobProgress value, CancellationToken cancellationToken)
    {
        if (value == null)
        {
            return;
        }

        _ = await _syncJobBlobContainerClient
                    .GetBlobClient($"{value.CorrelationId}.json")
                    .UploadAsync(
                        content: BinaryData.FromObjectAsJson(value),
                        overwrite: true,
                        cancellationToken: cancellationToken
                    );

        if (_queueClients.TryGetValue(value.State, out var _queueClient))
        {
            _ = await _queueClient.SendMessageAsync(value.CorrelationId, cancellationToken);
        }
    }

    public async Task Report(LogSchedule value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);

        _ = await _syncScheduleBlobContainerClient
                   .GetBlobClient($"{value.CorrelationId}.json")
                   .UploadAsync(
                       content: BinaryData.FromObjectAsJson(value),
                       overwrite: true,
                       cancellationToken: cancellationToken
                   );

        _ = await _logScheduleQueueClient.SendMessageAsync(value.CorrelationId, cancellationToken);
    }
}
