using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlBulkSyncFunction.Models;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions
{
    // ReSharper disable once UnusedMember.Global
    public record ProcessGlobalChangeTracking(
        ILogger<ProcessGlobalChangeTracking> Logger,
        IOptions<SyncJobsConfig> SyncJobsConfig,
        ITokenCacheService TokenCacheService
        )
    {
        [Function("ProcessGlobalChangeTracking")]
        public async Task<ProcessGlobalChangeTrackingResult> Run(
            [TimerTrigger("%ProcessGlobalChangeTrackingSchedule%")] TimerTriggerInfo timerTriggerInfo
            )
        {
            if (timerTriggerInfo.IsPastDue)
            {
                Logger.LogWarning("IsPastDue skipping.", timerTriggerInfo.IsPastDue);
                return new ProcessGlobalChangeTrackingResult(Array.Empty<SyncJob>());
            }

            var tokenCache = await TokenCacheService.GetTokenCache(SyncJobsConfig.Value);

            var expires = DateTimeOffset.UtcNow.AddMinutes(4);

            return new ProcessGlobalChangeTrackingResult(
                SyncJobsConfig
                    .Value
                    .Jobs
                    .Select(
                        job => new SyncJob(
                            SourceDbConnection: job.Source.ConnectionString,
                            SourceDbAccessToken: job.Source.ManagedIdentity && tokenCache.TryGetValue(job.Source.TenantId ?? string.Empty, out var sourceToken) ? sourceToken : null,
                            TargetDbConnection: job.Target.ConnectionString,
                            TargetDbAccessToken: job.Target.ManagedIdentity && tokenCache.TryGetValue(job.Target.TenantId ?? string.Empty, out var targetToken) ? targetToken : null,
                            Tables: job.Tables,
                            BatchSize: job.BatchSize,
                            Expires: expires
                        )
                ).ToArray()
            );
        }
    }
}
