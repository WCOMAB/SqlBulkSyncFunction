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
// ReSharper disable UnusedMember.Global

namespace SqlBulkSyncFunction.Functions
{
    // ReSharper disable once UnusedType.Global
    public record ProcessGlobalChangeTrackingSchedule(
        ILogger<ProcessGlobalChangeTrackingSchedule> Logger,
        IOptions<SyncJobsConfig> SyncJobsConfig,
        ITokenCacheService TokenCacheService
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
            using (Logger.BeginScope(config))
            {
                try
                {
                    if (timerInfo.IsPastDue)
                    {
                        Logger.LogWarning("{Config} IsPastDue ({IsPastDue}) skipping.", config, timerInfo.IsPastDue);
                        return ProcessGlobalChangeTrackingResult.Empty;
                    }

                    var expires = DateTimeOffset.UtcNow.AddMinutes(4);
                    var values = SyncJobsConfig?.Value?.Jobs?.Values;

                    if (values == null || values.Count == 0)
                    {
                        Logger.LogWarning("{Config} no jobs configured, skipping.", config);
                        return ProcessGlobalChangeTrackingResult.Empty;
                    }

                    var tokenCache = await TokenCacheService.GetTokenCache(values);

                    var syncJobs = SyncJobsConfig
                        .Value
                        .ScheduledJobs.Value[config]
                        .Select(job => job.Job.ToSyncJob(job.Key, config, tokenCache, expires, false))
                        .ToArray();

                    Logger.LogInformation("{Config} Found {Length} jobs for schedule.", config, syncJobs.Length);

                    return new ProcessGlobalChangeTrackingResult(syncJobs);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Failed to process {config}", config);
                    return ProcessGlobalChangeTrackingResult.Empty;
                }
            }
        }
    }
}
