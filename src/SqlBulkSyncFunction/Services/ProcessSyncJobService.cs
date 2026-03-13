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
        using (logger.BeginScope("Schedule={Schedule}, Id={Id}, Area={Area}", schedule, id, area))
        {
            await using SqlConnection
                sourceConn = new(syncJob.SourceDbConnection) { AccessToken = syncJob.SourceDbAccessToken },
                targetConn = new(syncJob.TargetDbConnection) { AccessToken = syncJob.TargetDbAccessToken };

            using IDisposable
                from = logger.BeginScope("{DataSource}.{Database}", sourceConn.DataSource, sourceConn.Database),
                to = logger.BeginScope("{DataSource}.{Database}", targetConn.DataSource, targetConn.Database);

            LogConnectingToSourceDatabase(schedule, id, area, sourceConn.DataSource, sourceConn.Database);
            await sourceConn.OpenAsync(cancellationToken);
            LogConnected(schedule, id, area, sourceConn.ClientConnectionId);

            LogConnectingToTargetDatabase(schedule, id, area, targetConn.DataSource, targetConn.Database);
            await targetConn.OpenAsync();
            LogConnected(schedule, id, area, targetConn.ClientConnectionId);

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
                        globalChangeTracking
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

                            if (syncJob.Seed)
                            {
                                SeedTable(targetConn, tableSchema, sourceConn, scope);
                            }
                            else if (tableSchema.SourceVersion == null)
                            {
                                LogUnknownSourceVersion(schedule, id, area, tableSchema.Scope);
                                return;
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

    private void SeedTable(SqlConnection targetConn, TableSchema tableSchema, SqlConnection sourceConn, object scope)
    {
        targetConn.TruncateTargetTable(tableSchema, scope, logger);
        sourceConn.BulkCopyDataDirect(targetConn, tableSchema, scope, logger);
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
