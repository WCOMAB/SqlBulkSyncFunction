using Microsoft.Azure.Functions.Worker;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Models
{
    public record ProcessGlobalChangeTrackingResult(
        [property: QueueOutput(SqlBulkSyncFunction.Constants.ProcessGlobalChangeTrackingQueue)] params SyncJob[] SyncJobs
        )
    {
        public static ProcessGlobalChangeTrackingResult Empty { get; } = new();
    }
}
