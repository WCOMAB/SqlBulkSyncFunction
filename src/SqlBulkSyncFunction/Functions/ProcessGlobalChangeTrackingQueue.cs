using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions
{
    // ReSharper disable once UnusedMember.Global
    public record ProcessGlobalChangeTrackingQueue(
        ILogger<ProcessGlobalChangeTrackingQueue> Logger,
        IProcessSyncJobService ProcessSyncJobService
        )
    {

        [Function(nameof(ProcessGlobalChangeTrackingQueue))]
        
        public async Task Run([QueueTrigger(nameof(ProcessGlobalChangeTrackingQueue))] SyncJob syncJob)
        {
            if(syncJob == null)
            {
                return;
            }

            var scope = new { syncJob.Schedule, syncJob.Id, syncJob.Area };
            using (Logger.BeginScope(scope))
            {
                if (syncJob.Expires < DateTimeOffset.UtcNow)
                {
                    Logger.LogWarning("Sync job {Scope} Schedule expired: {Expires}", scope, syncJob.Expires);
                    return;
                }

                await ProcessSyncJobService.ProcessSyncJob(
                    globalChangeTracking: true,
                    syncJob: syncJob
                );
            }
        }
    }
}
