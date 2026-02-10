using System;
using System.Collections.Concurrent;
using System.Linq;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Helpers;

public static class SyncJobConfigExtensions
{
    public static SyncJob ToSyncJob(
        this SyncJobConfig job,
        string id,
        string schedule,
        ConcurrentDictionary<string, string> tokenCache,
        DateTimeOffset expires,
        bool seed
    ) => new(
                id,
                schedule,
                job.Area,
                SourceDbConnection: job.Source.ConnectionString,
                SourceDbAccessToken: TryGetToken(job.Source, tokenCache),
                TargetDbConnection: job.Target.ConnectionString,
                TargetDbAccessToken: TryGetToken(job.Target, tokenCache),
                Tables: job.ToSyncJobTables(),
                BatchSize: job.BatchSize,
                Expires: expires,
                Seed: seed
            );

    private static SyncJobTable[] ToSyncJobTables(this SyncJobConfig job)
    {
        var targetTableLookup = job.TargetTables?.ToLookup(
            key => key.Key,
            value => value.Value,
            StringComparer.OrdinalIgnoreCase
        );

        var disableTargetIdentityInsertTables = job.DisableTargetIdentityInsertTables?.ToLookup(
            key => key.Key,
            value => value.Value,
            StringComparer.OrdinalIgnoreCase
        );

        return [.. job.Tables.Select(
            sourceTable => new SyncJobTable(
                sourceTable.Value,
                targetTableLookup?[sourceTable.Key].FirstOrDefault() switch
                {
                    { Length: > 0 } overrideTargetTable => overrideTargetTable,
                    _ => sourceTable.Value
                },
                disableTargetIdentityInsertTables?[sourceTable.Key].FirstOrDefault() ?? false
            )
        )];
    }

    private static string TryGetToken(SyncJobConfigDataSource dataSource, ConcurrentDictionary<string, string> tokenCache) => dataSource.ManagedIdentity && tokenCache.TryGetValue(dataSource.TenantId ?? string.Empty, out var sourceToken)
            ? sourceToken
            : null;
}
