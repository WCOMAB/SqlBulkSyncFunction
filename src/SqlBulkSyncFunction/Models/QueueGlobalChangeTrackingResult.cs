using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Models;

public record QueueGlobalChangeTrackingResult(
    [property:QueueOutput(Constants.ProcessGlobalChangeTrackingQueue)]
    SyncJob SyncJob,
    [property: HttpResult]
    HttpResponseData HttpResponseData
);
