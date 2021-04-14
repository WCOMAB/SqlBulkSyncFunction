using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Services
{
    // ReSharper disable once UnusedMember.Global
    public record TokenCacheService(IAzureSqlTokenService AzureSqlTokenService) : ITokenCacheService
    {
        public async Task<ConcurrentDictionary<string, string>> GetTokenCache(SyncJobsConfig syncJobsConfig)
        {
            var tokenCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await syncJobsConfig
                .Jobs
                .Select(
                    job => new[]
                    {
                        (job.Source.TenantId, job.Source.ManagedIdentity),
                        (job.Target.TenantId, job.Target.ManagedIdentity)
                    }
                )
                .SelectMany(tenant => tenant)
                .Where(tenant => tenant.ManagedIdentity)
                .Select(tenant => tenant.TenantId ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ParallelForEachAsync(
                    maxDegreeOfParallelism: 4,
                    asyncItemAction: async tenant =>
                    {
                        tokenCache.TryAdd(
                            tenant,
                            await AzureSqlTokenService.GetAccessToken(tenant)
                        );
                    }
                );
            return tokenCache;
        }
    }
}
