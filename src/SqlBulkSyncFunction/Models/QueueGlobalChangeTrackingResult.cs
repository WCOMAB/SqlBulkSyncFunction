using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SqlBulkSyncFunction.Functions;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Models
{
    public record QueueGlobalChangeTrackingResult(
        [property:QueueOutput(nameof(ProcessGlobalChangeTrackingQueue))]
        SyncJob SyncJob,
        HttpResponseData HttpResponseData
    );
}