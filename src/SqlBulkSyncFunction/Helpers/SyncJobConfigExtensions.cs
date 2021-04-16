using System;
using System.Collections.Concurrent;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Helpers
{
    public static class SyncJobConfigExtensions
    {
        public static SyncJob ToSyncJob(
            this SyncJobConfig job,
            string id,
            ConcurrentDictionary<string, string> tokenCache,
            DateTimeOffset expires
        ) => new (
                    id,
                    job.Area,
                    SourceDbConnection: job.Source.ConnectionString,
                    SourceDbAccessToken: TryGetToken(job.Source, tokenCache),
                    TargetDbConnection: job.Target.ConnectionString,
                    TargetDbAccessToken: TryGetToken(job.Target, tokenCache),
                    Tables: job.Tables,
                    BatchSize: job.BatchSize,
                    Expires: expires
                );

        private static string TryGetToken(SyncJobConfigDataSource dataSource, ConcurrentDictionary<string, string> tokenCache)
        {
            return dataSource.ManagedIdentity && tokenCache.TryGetValue(dataSource.TenantId ?? string.Empty, out var sourceToken)
                ? sourceToken
                : null;
        }
    }
}
