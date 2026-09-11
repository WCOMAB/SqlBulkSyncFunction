using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
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
                SourceDbConnection: job.Source.ConnectionString.WithApplicationName(),
                SourceDbAccessToken: TryGetToken(job.Source, tokenCache),
                TargetDbConnection: job.Target.ConnectionString.WithApplicationName(),
                TargetDbAccessToken: TryGetToken(job.Target, tokenCache),
                Tables: job.ToSyncJobTables(),
                BatchSize: job.BatchSize,
                UseSnapshotIsolationSeed: job.UseSnapshotIsolationSeed,
                UseApplicationIntentReadOnlySeed: job.UseApplicationIntentReadOnlySeed,
                FullSync: job.FullSync,
                Timestamp: timestamp,
                Expires: expires,
                Seed: seed
            );

    /// <summary>
    /// Ensures the connection string identifies the app to SQL Server, unless it already sets Application Name.
    /// </summary>
    private static string WithApplicationName(this string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var builder = new SqlConnectionStringBuilder(connectionString);

        if (builder.ShouldSerialize(Constants.Sql.ApplicationNameKeyword))
        {
            return connectionString;
        }

        builder.ApplicationName = Constants.Sql.ApplicationName;
        return builder.ConnectionString;
    }

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

        var deleteInsteadOfTruncateTables = job.DeleteInsteadOfTruncateTables?.ToLookup(
            key => key.Key,
            value => value.Value,
            StringComparer.OrdinalIgnoreCase
        );

        var reseedTargetIdentityAfterClearTables = job.ReseedTargetIdentityAfterClearTables?.ToLookup(
            key => key.Key,
            value => value.Value,
            StringComparer.OrdinalIgnoreCase
        );

        return [.. job.Tables.Select(
            sourceTable => new SyncJobTable(
                sourceTable.Key,
                sourceTable.Value,
                targetTableLookup?[sourceTable.Key].FirstOrDefault() switch
                {
                    { Length: > 0 } overrideTargetTable => overrideTargetTable,
                    _ => sourceTable.Value
                },
                disableTargetIdentityInsertTables.GetValueOrDefault(sourceTable.Key),
                disableConstraintCheckTables.GetValueOrDefault(sourceTable.Key),
                deleteInsteadOfTruncateTables.GetValueOrDefault(sourceTable.Key),
                reseedTargetIdentityAfterClearTables.GetValueOrDefault(sourceTable.Key)
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
