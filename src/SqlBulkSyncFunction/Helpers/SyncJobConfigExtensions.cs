using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SqlBulkSyncFunction.Models;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Helpers;

public static class SyncJobConfigExtensions
{
    public static SyncJob ToSyncJob(
        this SyncJobConfig job,
        string scheduleCorrelationId,
        string id,
        string schedule,
        ConcurrentDictionary<string, string> tokenCache,
        DateTimeOffset timestamp,
        DateTimeOffset expires,
        bool seed
    ) => new(
                scheduleCorrelationId,
                id,
                schedule,
                job.Area,
                SourceDbConnection: job.Source.ConnectionString,
                SourceDbAccessToken: TryGetToken(job.Source, tokenCache),
                TargetDbConnection: job.Target.ConnectionString,
                TargetDbAccessToken: TryGetToken(job.Target, tokenCache),
                Tables: job.ToSyncJobTables(),
                BatchSize: job.BatchSize,
                Timestamp: timestamp,
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

        var disableConstraintCheckTables = job.DisableConstraintCheckTables?.ToLookup(
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
                disableTargetIdentityInsertTables.GetValueOrDefault(sourceTable.Key),
                disableConstraintCheckTables.GetValueOrDefault(sourceTable.Key)
            )
        )];
    }

    private static bool GetValueOrDefault(this ILookup<string, bool> lookup, string key, bool defaultValue = false)
        => lookup?[key].FirstOrDefault() ?? defaultValue;

    /// <summary>
    /// Gets the boolean value for the specified key, returning false if key doesn't exist or value is false.
    /// </summary>
    public static bool GetValueOrDefault(this Dictionary<string, bool> dictionary, string key)
        => dictionary?.TryGetValue(key, out var value) == true && value;

    private static string TryGetToken(SyncJobConfigDataSource dataSource, ConcurrentDictionary<string, string> tokenCache) => dataSource.ManagedIdentity && tokenCache.TryGetValue(dataSource.TenantId ?? string.Empty, out var sourceToken)
            ? sourceToken
            : null;

    public static LogSyncJob[] ToLogSyncJobs(this SyncJob[] syncJobs)
        => [.. syncJobs.Select(ToLogSyncJob)];

    public static  LogSyncJob ToLogSyncJob(this SyncJob syncJob)
        => new(
        syncJob.CorrelationId,
        syncJob.Id,
        syncJob.Area,
        syncJob.Seed
    );
}
