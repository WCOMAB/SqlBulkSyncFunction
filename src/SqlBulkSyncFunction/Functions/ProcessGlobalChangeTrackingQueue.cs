using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Models;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions;

public partial class ProcessGlobalChangeTrackingQueue(
    ILogger<ProcessGlobalChangeTrackingQueue> logger,
    IProcessSyncJobService processSyncJobService,
    SyncProgressService syncProgressService
    )
{
    [Function(nameof(ProcessGlobalChangeTrackingQueue))]

    public async Task Run(
        [QueueTrigger(Constants.Queues.ProcessGlobalChangeTrackingQueue)] SyncJob syncJob,
        CancellationToken cancellationToken
        )
    {
        if (syncJob == null)
        {
            return;
        }


        using (logger.BeginScope("Schedule={Schedule}, Id={Id}, Area={Area}", syncJob.Schedule, syncJob.Id, syncJob.Area))
        {
            var initialProgress = new SyncJobProgress(
                Area: syncJob.Area,
                ConfigurationId: syncJob.Id,
                Schedule: syncJob.Schedule,
                ScheduleCorrelationId: syncJob.ScheduleCorrelationId,
                SyncJobCorrelationId: syncJob.CorrelationId,
                State: SyncJobProgressState.Started
            );

            if (syncJob.Expires < DateTimeOffset.UtcNow)
            {
                LogScheduleExpired(syncJob.Schedule, syncJob.Id, syncJob.Area, syncJob.Expires);

                await syncProgressService.Report(
                    initialProgress with
                    {
                        State = SyncJobProgressState.Expired,
                        Message = FormattableString.Invariant($"Schedule expired at {syncJob.Expires}")
                    },
                    cancellationToken
                   );

                return;
            }

            await syncProgressService.Report(
                    initialProgress,
                    cancellationToken
                );

            try
            {
                await processSyncJobService.ProcessSyncJob(
                    globalChangeTracking: true,
                    syncJob: syncJob,
                    cancellationToken: cancellationToken
                );

                await syncProgressService.Report(
                    initialProgress.WithState(
                        SyncJobProgressState.Done
                    ),
                    cancellationToken
                );
            }
            catch(Exception ex)
            {

                await syncProgressService.Report(
                        initialProgress.WithState(
                        SyncJobProgressState.Exception,
                        ex.ToString()
                        ),
                        cancellationToken
                    );

                throw;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sync job Schedule={Schedule}, Id={Id}, Area={Area} expired: {Expires}")]
    private partial void LogScheduleExpired(string schedule, string id, string area, DateTimeOffset expires);
}
