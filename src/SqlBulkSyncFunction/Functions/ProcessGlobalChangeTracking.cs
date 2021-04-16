using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlBulkSyncFunction.Models;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;
using SqlBulkSyncFunction.Helpers;

namespace SqlBulkSyncFunction.Functions
{
    // ReSharper disable once UnusedMember.Global
    public record ProcessGlobalChangeTracking(
        ILogger<ProcessGlobalChangeTracking> Logger,
        IOptions<SyncJobsConfig> SyncJobsConfig,
        ITokenCacheService TokenCacheService
        )
    {
        [Function(nameof(ProcessGlobalChangeTracking))]
        public async Task<ProcessGlobalChangeTrackingResult> Run(
            [TimerTrigger("%ProcessGlobalChangeTrackingSchedule%")] TimerTriggerInfo timerTriggerInfo
            )
        {
            if (timerTriggerInfo.IsPastDue)
            {
                Logger.LogWarning("IsPastDue skipping.", timerTriggerInfo.IsPastDue);
                return new ProcessGlobalChangeTrackingResult(Array.Empty<SyncJob>());
            }

            var expires = DateTimeOffset.UtcNow.AddMinutes(4);
            var tokenCache = await TokenCacheService.GetTokenCache(SyncJobsConfig.Value.Jobs.Values);

            return new ProcessGlobalChangeTrackingResult(
                SyncJobsConfig
                    .Value
                    .Jobs
                    .Where(job => !job.Value.Manual.HasValue || job.Value.Manual==false)
                    .Select(job => job.Value.ToSyncJob(job.Key, tokenCache, expires))
                    .ToArray()
            );
        }
    }
}
