using Microsoft.Azure.Functions.Worker;
using SqlBulkSyncFunction.Functions;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Models
{
    public record ProcessGlobalChangeTrackingResult(
        [property:QueueOutput(nameof(ProcessGlobalChangeTrackingQueue))]SyncJob[] SyncJobs
        );
}