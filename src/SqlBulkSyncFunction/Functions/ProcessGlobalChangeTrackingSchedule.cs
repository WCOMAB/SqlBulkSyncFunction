using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlBulkSyncFunction;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions;

public partial class ProcessGlobalChangeTrackingSchedule(
    ILogger<ProcessGlobalChangeTrackingSchedule> logger,
    IOptions<SyncJobsConfig> syncJobsConfig,
    ITokenCacheService tokenCacheService,
    IProcessSyncJobService processSyncJobService,
    SyncProgressService syncProgressService
    )
{
    private const string FunctionName = nameof(ProcessGlobalChangeTrackingSchedule);

    [Function(FunctionName + nameof(Custom))]
    public Task Custom(
        [TimerTrigger(Constants.Schedules.CustomScheduleTimerTrigger)]
        TimerInfo timerInfo,
        CancellationToken cancellationToken
    ) => ProcessSchedule(timerInfo, cancellationToken);

    [Function(FunctionName + nameof(Midnight))]
    public Task Midnight(
        [TimerTrigger(Constants.Schedules.MidnightCron)] TimerInfo timerInfo,
        CancellationToken cancellationToken
    ) => ProcessSchedule(timerInfo, cancellationToken);

    [Function(FunctionName + nameof(Noon))]
    public Task Noon(
        [TimerTrigger(Constants.Schedules.NoonCron)] TimerInfo timerInfo,
        CancellationToken cancellationToken
    ) => ProcessSchedule(timerInfo, cancellationToken);

    [Function(FunctionName + nameof(EveryFiveMinutes))]
    public Task EveryFiveMinutes(
        [TimerTrigger(Constants.Schedules.EveryFiveMinutesCron)] TimerInfo timerInfo,
        CancellationToken cancellationToken
    ) => ProcessSchedule(timerInfo, cancellationToken);

    [Function(FunctionName + nameof(EveryHour))]
    public Task EveryHour(
        [TimerTrigger(Constants.Schedules.EveryHourCron)] TimerInfo timerInfo,
        CancellationToken cancellationToken
    ) => ProcessSchedule(timerInfo, cancellationToken);


    private async Task ProcessSchedule(
        TimerInfo timerInfo,
        CancellationToken cancellationToken,
        [CallerMemberName]
        string scheduleName = null
        )
    {
        using (logger.BeginScope(scheduleName))
        {
            LogSchedule logSchedule = new(
                        scheduleName,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow.AddMinutes(4),
                        timerInfo.IsPastDue,
                        timerInfo?.ScheduleStatus?.Last,
                        timerInfo?.ScheduleStatus?.Next,
                        timerInfo?.ScheduleStatus?.LastUpdated
                        );

            try
            {
                if (timerInfo.IsPastDue && scheduleName == nameof(EveryFiveMinutes))
                {
                    LogIsPastDueSkipping(scheduleName, timerInfo.IsPastDue);
                    return;
                }

                var values = syncJobsConfig?.Value?.Jobs?.Values;

                if (values == null || values.Count == 0)
                {
                    LogNoJobsConfiguredSkipping(scheduleName);
                    return;
                }

                var tokenCache = await tokenCacheService.GetTokenCache(values);

                SyncJob[] syncJobs = [
                                        ..
                                        syncJobsConfig
                                            .Value
                                            .ScheduledJobs.Value[scheduleName]
                                            .Select(job => job.Job.Value.ToSyncJob(logSchedule.CorrelationId, job.Job.Key, scheduleName, tokenCache, logSchedule.Timestamp, logSchedule.Expires, false))
                                    ];

                LogFoundJobsForSchedule(scheduleName, syncJobs.Length);


                await syncProgressService.Report(
                    logSchedule with
                    {
                        SyncJobs = syncJobs.ToLogSyncJobs()
                    },
                    cancellationToken
                );

                foreach (var syncJob in syncJobs)
                {
                    var createdProgress = new SyncJobProgress(
                        Area: syncJob.Area,
                        ConfigurationId: syncJob.Id,
                        Schedule: syncJob.Schedule,
                        ScheduleCorrelationId: syncJob.ScheduleCorrelationId,
                        SyncJobCorrelationId: syncJob.CorrelationId,
                        State: SyncJobProgressState.Created
                    );

                    await syncProgressService.Report(createdProgress, cancellationToken);

                    await processSyncJobService.EnqueueSyncJob(syncJob, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                LogFailedToProcess(ex, scheduleName);
                return;
            }
        }
    }
}
