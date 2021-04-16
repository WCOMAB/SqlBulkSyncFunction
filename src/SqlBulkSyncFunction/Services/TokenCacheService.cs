﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Services
{
    // ReSharper disable once UnusedMember.Global
    public record TokenCacheService(IAzureSqlTokenService AzureSqlTokenService) : ITokenCacheService
    {
        public Task<ConcurrentDictionary<string, string>> GetTokenCache(IEnumerable<SyncJobConfig> jobs)
            => GetTokenCache(
                jobs
                    .Select(
                        job => new[]
                        {
                            job.Source,
                            job.Target
                        }
                    )
                    .SelectMany(tenant => tenant)
            );

        public Task<ConcurrentDictionary<string, string>> GetTokenCache(SyncJobConfig job)
            => GetTokenCache(
                new[]
                {
                    job.Source,
                    job.Target
                }
            );

        private async Task<ConcurrentDictionary<string, string>> GetTokenCache(IEnumerable<SyncJobConfigDataSource> dataSources)
        {
            var tokenCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await dataSources
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
