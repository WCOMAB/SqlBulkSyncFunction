using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Models
{
    public record QueueGlobalChangeTrackingResult(
        [property:QueueOutput(SqlBulkSyncFunction.Constants.ProcessGlobalChangeTrackingQueue)]
        SyncJob SyncJob,
        HttpResponseData HttpResponseData
    );
}
