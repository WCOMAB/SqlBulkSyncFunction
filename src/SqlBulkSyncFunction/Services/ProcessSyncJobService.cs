using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Models.Schema;

namespace SqlBulkSyncFunction.Services;

public partial class ProcessSyncJobService(
    ILogger<ProcessSyncJobService> logger,
    QueueServiceClient queueServiceClient
    ) : IProcessSyncJobService
{
    private readonly QueueClient _queueClient = GetSyncProgressQueueClient(queueServiceClient);

    private static QueueClient GetSyncProgressQueueClient(QueueServiceClient queueServiceClient)
    {
        var queueClient = queueServiceClient.GetQueueClient(Constants.Queues.ProcessGlobalChangeTrackingQueue);
        queueClient.CreateIfNotExists();
        return queueClient;
    }

    public async Task EnqueueSyncJob(SyncJob syncJob, CancellationToken cancellationToken)
        => await _queueClient.SendMessageAsync(BinaryData.FromObjectAsJson(syncJob), cancellationToken: cancellationToken);

    public async Task ProcessSyncJob(SyncJob syncJob, bool globalChangeTracking, CancellationToken cancellationToken)
    {
        var (schedule, id, area) = (syncJob.Schedule, syncJob.Id, syncJob.Area);
        var scope = new { Schedule = schedule, Id = id, Area = area };
        var fullSync = syncJob.FullSync;

        if (fullSync is not null && fullSync.IntervalMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                $"{scope} FullSync is configured but IntervalMilliseconds must be greater than 0 (got {fullSync.IntervalMilliseconds})."
                );
        }

        using (logger.BeginScope("Schedule={Schedule}, Id={Id}, Area={Area}", schedule, id, area))
        {
            await using SqlConnection
                sourceConn = new(syncJob.SourceDbConnection) { AccessToken = syncJob.SourceDbAccessToken },
                targetConn = new(syncJob.TargetDbConnection) { AccessToken = syncJob.TargetDbAccessToken },
                bulkSourceConnOverride = CreateReadOnlyBulkSourceConnectionOrNull(syncJob);

            using IDisposable
                from = logger.BeginScope("{DataSource}.{Database}", sourceConn.DataSource, sourceConn.Database),
                to = logger.BeginScope("{DataSource}.{Database}", targetConn.DataSource, targetConn.Database);

            LogConnectingToSourceDatabase(schedule, id, area, sourceConn.DataSource, sourceConn.Database);
            await sourceConn.OpenAsync(cancellationToken);
            LogConnected(schedule, id, area, sourceConn.ClientConnectionId);

            LogConnectingToTargetDatabase(schedule, id, area, targetConn.DataSource, targetConn.Database);
            await targetConn.OpenAsync();
            LogConnected(schedule, id, area, targetConn.ClientConnectionId);

            if (bulkSourceConnOverride is not null)
            {
                LogConnectingToReadOnlySeedSource(schedule, id, area, bulkSourceConnOverride.DataSource, bulkSourceConnOverride.Database);
                await bulkSourceConnOverride.OpenAsync(cancellationToken);
                LogConnected(schedule, id, area, bulkSourceConnOverride.ClientConnectionId);
            }

            var bulkSourceConn = bulkSourceConnOverride ?? sourceConn;
            var useSnapshotIsolation = fullSync?.UseSnapshotIsolation ?? syncJob.UseSnapshotIsolationSeed;
            var useUnixEpochVersion = fullSync is not null;

            LogEnsuringSyncSchemaAndTableExists(schedule, id, area);
            targetConn.EnsureSyncSchemaAndTableExists(scope, logger);
            LogEnsuredSyncSchemaAndTableExist(schedule, id, area);

            LogFetchingTableSchemas(schedule, id, area);
            var schemaStopWatch = Stopwatch.StartNew();
            var tableSchemas = (
                    syncJob.Tables ?? []
                )
                .Select(
                    table => TableSchema.LoadSchema(
                        sourceConn,
                        targetConn,
                        table,
                        syncJob.BatchSize,
                        globalChangeTracking,
                        useSnapshotIsolation,
                        useUnixEpochVersion
                        )
                ).ToArray();
            schemaStopWatch.Stop();
            LogFoundTablesDuration(schedule, id, area, tableSchemas.Length, schemaStopWatch.Elapsed);
            var exceptions = new List<Exception>();
            Array.ForEach(
                tableSchemas,
                tableSchema =>
                {
                    var syncStopWatch = Stopwatch.StartNew();
                    try
                    {
                        using (logger.BeginScope("{TableSchemaScope}", tableSchema.Scope))
                        {
                            LogBeginTableSchemaScope(schedule, id, area, tableSchema.Scope);

                            if (tableSchema.SourceVersion is null)
                            {
                                LogUnknownSourceVersion(schedule, id, area, tableSchema.Scope);
                                return;
                            }

                            if (fullSync is not null)
                            {
                                ProcessFullSyncTable(
                                    syncJob,
                                    fullSync,
                                    tableSchema,
                                    targetConn,
                                    bulkSourceConn,
                                    scope,
                                    schedule,
                                    id,
                                    area,
                                    syncStopWatch
                                    );
                            }
                            else
                            {
                                ProcessChangeTrackingTable(
                                    syncJob,
                                    tableSchema,
                                    targetConn,
                                    sourceConn,
                                    bulkSourceConn,
                                    scope,
                                    schedule,
                                    id,
                                    area,
                                    syncStopWatch
                                    );
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        syncStopWatch.Stop();
                        LogSyncException(ex, schedule, id, area, tableSchema.Scope, syncStopWatch.Elapsed, ex.Message);

                        exceptions.Add(ex);
                    }
                }
            );

            if (exceptions.Count != 0)
            {
                throw new AggregateException($"{scope} sync failed", exceptions);
            }
        }
    }

    private void ProcessFullSyncTable(
        SyncJob syncJob,
        SyncJobFullSyncConfig fullSync,
        TableSchema tableSchema,
        SqlConnection targetConn,
        SqlConnection bulkSourceConn,
        object scope,
        string schedule,
        string id,
        string area,
        Stopwatch syncStopWatch
        )
    {
        var due = syncJob.Seed
            || tableSchema.TargetVersion.CurrentVersion < 0
            || (tableSchema.SourceVersion.CurrentVersion - tableSchema.TargetVersion.CurrentVersion) >= fullSync.IntervalMilliseconds;

        if (!due)
        {
            LogFullSyncIntervalNotDue(
                schedule,
                id,
                area,
                fullSync.IntervalMilliseconds,
                tableSchema.TargetVersion.CurrentVersion,
                tableSchema.SourceVersion.CurrentVersion
                );
            syncStopWatch.Stop();
            LogEndTableSchemaScopeDuration(schedule, id, area, tableSchema.Scope, syncStopWatch.Elapsed);
            return;
        }

        if (syncJob.Seed)
        {
            SeedTable(targetConn, tableSchema, bulkSourceConn, scope);
        }
        else
        {
            FullSyncReconcileTable(targetConn, tableSchema, bulkSourceConn, scope);
        }

        syncStopWatch.Stop();
        LogEndTableSchemaScopeDuration(schedule, id, area, tableSchema.Scope, syncStopWatch.Elapsed);
        targetConn.PersistsSourceTargetVersionState(tableSchema);
    }

    private void ProcessChangeTrackingTable(
        SyncJob syncJob,
        TableSchema tableSchema,
        SqlConnection targetConn,
        SqlConnection sourceConn,
        SqlConnection bulkSourceConn,
        object scope,
        string schedule,
        string id,
        string area,
        Stopwatch syncStopWatch
        )
    {
        if (syncJob.Seed)
        {
            SeedTable(targetConn, tableSchema, bulkSourceConn, scope);
        }
        else if (tableSchema.SourceVersion.CurrentVersion.Equals(tableSchema.TargetVersion.CurrentVersion))
        {
            LogAlreadyUpToDate(schedule, id, area);
        }
        else
        {
            SyncTable(targetConn, tableSchema, sourceConn, scope);
        }

        syncStopWatch.Stop();
        LogEndTableSchemaScopeDuration(schedule, id, area, tableSchema.Scope, syncStopWatch.Elapsed);
        targetConn.PersistsSourceTargetVersionState(tableSchema);
    }

    private static SqlConnection CreateReadOnlyBulkSourceConnectionOrNull(SyncJob syncJob)
    {
        var useReadOnly = syncJob.FullSync is not null
            ? syncJob.FullSync.UseApplicationIntentReadOnly
            : syncJob.Seed && syncJob.UseApplicationIntentReadOnlySeed;

        if (!useReadOnly)
        {
            return null;
        }

        var connectionString = new SqlConnectionStringBuilder(syncJob.SourceDbConnection)
        {
            ApplicationIntent = ApplicationIntent.ReadOnly
        }.ConnectionString;

        return new SqlConnection(connectionString) { AccessToken = syncJob.SourceDbAccessToken };
    }

    private void SeedTable(SqlConnection targetConn, TableSchema tableSchema, SqlConnection sourceConn, object scope)
    {
        targetConn.ClearTargetTable(tableSchema, scope, logger);
        sourceConn.BulkCopyDataDirect(targetConn, tableSchema, scope, logger);
    }

    private void FullSyncReconcileTable(
        SqlConnection targetConn,
        TableSchema tableSchema,
        SqlConnection sourceConn,
        object scope
        )
    {
        if (targetConn.SyncTablesExist(tableSchema))
        {
            throw new Exception($"{scope} Aborting! Sync tables already exists ({tableSchema.SyncNewOrUpdatedTableName}, {tableSchema.SyncDeletedTableName})");
        }

        try
        {
            targetConn.CreateNewOrUpdatedSyncTable(tableSchema, scope, logger);
            sourceConn.BulkCopyAllToSyncNewOrUpdatedTable(targetConn, tableSchema, scope, logger);
            targetConn.ReconcileFullSyncData(tableSchema, scope, logger);
        }
        finally
        {
            targetConn.DropNewOrUpdatedSyncTable(tableSchema, scope, logger);
        }
    }

    private void SyncTable(SqlConnection targetConn, TableSchema tableSchema, SqlConnection sourceConn, object scope)
    {
        if (targetConn.SyncTablesExist(tableSchema))
        {
            throw new Exception($"{scope} Aborting! Sync tables already exists ({tableSchema.SyncNewOrUpdatedTableName}, {tableSchema.SyncDeletedTableName})");
        }
        try
        {
            targetConn.CreateSyncTables(tableSchema, scope, logger);
            sourceConn.BulkCopyData(targetConn, tableSchema, scope, logger);
            targetConn.DeleteData(tableSchema, scope, logger);
            targetConn.MergeData(tableSchema, scope, logger);
        }
        finally
        {
            targetConn.DropSyncTables(tableSchema, scope, logger);
        }
    }
}
