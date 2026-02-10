using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions;

public partial class ProcessGlobalChangeTrackingSchedule(
    ILogger<ProcessGlobalChangeTrackingSchedule> logger,
    IOptions<SyncJobsConfig> syncJobsConfig,
    ITokenCacheService tokenCacheService
    )
{
    private const string FunctionName = nameof(ProcessGlobalChangeTrackingSchedule);

    [Function(FunctionName + nameof(Custom))]
    public Task<ProcessGlobalChangeTrackingResult> Custom(
        [TimerTrigger("%ProcessGlobalChangeTrackingSchedule%")]
        TimerInfo timerInfo
    ) => ProcessSchedule(timerInfo);

    [Function(FunctionName + nameof(Midnight))]
    public Task<ProcessGlobalChangeTrackingResult> Midnight(
        [TimerTrigger("0 0 0 * * *")] TimerInfo timerInfo
    ) => ProcessSchedule(timerInfo);

    [Function(FunctionName + nameof(Noon))]
    public Task<ProcessGlobalChangeTrackingResult> Noon(
        [TimerTrigger("0 0 12 * * *")] TimerInfo timerInfo
    ) => ProcessSchedule(timerInfo);

    [Function(FunctionName + nameof(EveryFiveMinutes))]
    public Task<ProcessGlobalChangeTrackingResult> EveryFiveMinutes(
        [TimerTrigger("5 */5 * * * *")] TimerInfo timerInfo
    ) => ProcessSchedule(timerInfo);

    [Function(FunctionName + nameof(EveryHour))]
    public Task<ProcessGlobalChangeTrackingResult> EveryHour(
        [TimerTrigger("10 0 * * * *")] TimerInfo timerInfo
    ) => ProcessSchedule(timerInfo);


    private async Task<ProcessGlobalChangeTrackingResult> ProcessSchedule(
        TimerInfo timerInfo,
        [CallerMemberName]
        string config = null
        )
    {
        using (logger.BeginScope(config))
        {
            try
            {
                if (timerInfo.IsPastDue)
                {
                    LogIsPastDueSkipping(config, timerInfo.IsPastDue);
                    return ProcessGlobalChangeTrackingResult.Empty;
                }

                var expires = DateTimeOffset.UtcNow.AddMinutes(4);
                var values = syncJobsConfig?.Value?.Jobs?.Values;

                if (values == null || values.Count == 0)
                {
                    LogNoJobsConfiguredSkipping(config);
                    return ProcessGlobalChangeTrackingResult.Empty;
                }

                var tokenCache = await tokenCacheService.GetTokenCache(values);

                var syncJobs = syncJobsConfig
                    .Value
                    .ScheduledJobs.Value[config]
                    .Select(job => job.Job.ToSyncJob(job.Key, config, tokenCache, expires, false))
                    .ToArray();

                LogFoundJobsForSchedule(config, syncJobs.Length);

                return new ProcessGlobalChangeTrackingResult(syncJobs);
            }
            catch (Exception ex)
            {
                LogFailedToProcess(ex, config);
                return ProcessGlobalChangeTrackingResult.Empty;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Config} IsPastDue ({IsPastDue}) skipping.")]
    private partial void LogIsPastDueSkipping(string config, bool isPastDue);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Config} no jobs configured, skipping.")]
    private partial void LogNoJobsConfiguredSkipping(string config);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Config} Found {Length} jobs for schedule.")]
    private partial void LogFoundJobsForSchedule(string config, int length);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process {Config}")]
    private partial void LogFailedToProcess(Exception ex, string config);
}
