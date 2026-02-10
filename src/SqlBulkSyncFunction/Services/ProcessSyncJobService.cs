using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Models.Schema;

namespace SqlBulkSyncFunction.Services;

public partial class ProcessSyncJobService(
    ILogger<ProcessSyncJobService> logger
    ) : IProcessSyncJobService
{
    public async Task ProcessSyncJob(SyncJob syncJob, bool globalChangeTracking)
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
            await sourceConn.OpenAsync();
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
                            else if (tableSchema.SourceVersion.Equals(tableSchema.TargetVersion))
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

            if (exceptions.Any())
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

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Connecting to source database {DataSource}.{Database}")]
    private partial void LogConnectingToSourceDatabase(string schedule, string id, string area, string dataSource, string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Connected {ClientConnectionId}")]
    private partial void LogConnected(string schedule, string id, string area, Guid clientConnectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Connecting to target database {DataSource}.{Database}")]
    private partial void LogConnectingToTargetDatabase(string schedule, string id, string area, string dataSource, string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Ensuring sync schema and table exists...")]
    private partial void LogEnsuringSyncSchemaAndTableExists(string schedule, string id, string area);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Ensured sync schema and table exist")]
    private partial void LogEnsuredSyncSchemaAndTableExist(string schedule, string id, string area);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Fetching table schemas...")]
    private partial void LogFetchingTableSchemas(string schedule, string id, string area);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Found {TableCount} tables, duration {Elapsed}")]
    private partial void LogFoundTablesDuration(string schedule, string id, string area, int tableCount, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Begin {TableSchemaScope}")]
    private partial void LogBeginTableSchemaScope(string schedule, string id, string area, string tableSchemaScope);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Already up to date")]
    private partial void LogAlreadyUpToDate(string schedule, string id, string area);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} End {TableSchemaScope}, duration {Elapsed}")]
    private partial void LogEndTableSchemaScopeDuration(string schedule, string id, string area, string tableSchemaScope, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Schedule} {Id} {Area} Exception {TableSchemaScope}, duration {Elapsed}, exception: {Message}")]
    private partial void LogSyncException(Exception ex, string schedule, string id, string area, string tableSchemaScope, TimeSpan elapsed, string message);
}
