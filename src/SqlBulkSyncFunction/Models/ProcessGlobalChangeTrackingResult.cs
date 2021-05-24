using Microsoft.Azure.Functions.Worker;
using SqlBulkSyncFunction.Functions;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Models
{
    public record ProcessGlobalChangeTrackingResult(
        [property:QueueOutput(nameof(ProcessGlobalChangeTrackingQueue))]params SyncJob[] SyncJobs
        )
    {
        public static ProcessGlobalChangeTrackingResult Empty { get; } = new();
    }
}