using Microsoft.Azure.Functions.Worker;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Models;

public record ProcessGlobalChangeTrackingResult(
    [property: QueueOutput(Constants.ProcessGlobalChangeTrackingQueue)] params SyncJob[] SyncJobs
    )
{
    public static ProcessGlobalChangeTrackingResult Empty { get; } = new();
}
