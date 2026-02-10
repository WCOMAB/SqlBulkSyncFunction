using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions;

public partial class ProcessGlobalChangeTrackingQueue(
    ILogger<ProcessGlobalChangeTrackingQueue> logger,
    IProcessSyncJobService processSyncJobService
    )
{
    [Function(nameof(ProcessGlobalChangeTrackingQueue))]

    public async Task Run([QueueTrigger(Constants.ProcessGlobalChangeTrackingQueue)] SyncJob syncJob)
    {
        if (syncJob == null)
        {
            return;
        }

        using (logger.BeginScope("Schedule={Schedule}, Id={Id}, Area={Area}", syncJob.Schedule, syncJob.Id, syncJob.Area))
        {
            if (syncJob.Expires < DateTimeOffset.UtcNow)
            {
                LogScheduleExpired(syncJob.Schedule, syncJob.Id, syncJob.Area, syncJob.Expires);
                return;
            }

            await processSyncJobService.ProcessSyncJob(
                globalChangeTracking: true,
                syncJob: syncJob
            );
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sync job Schedule={Schedule}, Id={Id}, Area={Area} expired: {Expires}")]
    private partial void LogScheduleExpired(string schedule, string id, string area, DateTimeOffset expires);
}
