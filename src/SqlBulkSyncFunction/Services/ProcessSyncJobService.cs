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

namespace SqlBulkSyncFunction.Services
{
    // ReSharper disable once UnusedMember.Global
    public record ProcessSyncJobService(ILogger<ProcessSyncJobService> Logger) : IProcessSyncJobService
    {
        public async Task ProcessSyncJob(SyncJob syncJob, bool globalChangeTracking)
        {
            var scope = new { syncJob.Schedule, syncJob.Id, syncJob.Area };
            using (Logger.BeginScope(scope))
            {
                await using SqlConnection
                    sourceConn = new(syncJob.SourceDbConnection) { AccessToken = syncJob.SourceDbAccessToken },
                    targetConn = new(syncJob.TargetDbConnection) { AccessToken = syncJob.TargetDbAccessToken };

                using IDisposable from = Logger.BeginScope($"{sourceConn.DataSource}.{sourceConn.Database}"),
                    to = Logger.BeginScope($"{targetConn.DataSource}.{targetConn.Database}");

                Logger.LogInformation(
                    "{Scope} Connecting to source database {DataSource}.{Database}",
                    scope,
                    sourceConn.DataSource,
                    sourceConn.Database
                );
                await sourceConn.OpenAsync();
                Logger.LogInformation("{Scope} Connected {ClientConnectionId}", scope, sourceConn.ClientConnectionId);

                Logger.LogInformation(
                    "{Scope} Connecting to target database {DataSource}.{Database}",
                    scope,
                    targetConn.DataSource,
                    targetConn.Database
                );
                await targetConn.OpenAsync();
                Logger.LogInformation("{Scope} Connected {ClientConnectionId}", scope, targetConn.ClientConnectionId);

                Logger.LogInformation("{Scope} Ensuring sync schema and table exists...", scope);
                targetConn.EnsureSyncSchemaAndTableExists(scope, Logger);
                Logger.LogInformation("{Scope} Ensured sync schema and table exist", scope);

                Logger.LogInformation("{Scope} Fetching table schemas...", scope);
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
                Logger.LogInformation("{Scope} Found {0} tables, duration {1}", scope, tableSchemas.Length, schemaStopWatch.Elapsed);
                var exceptions = new List<Exception>();
                Array.ForEach(
                    tableSchemas,
                    tableSchema =>
                    {
                        var syncStopWatch = Stopwatch.StartNew();
                        try
                        {
                            using (Logger.BeginScope(tableSchema.Scope))
                            {
                                Logger.LogInformation("{Scope} Begin {TableSchemaScope}", scope, tableSchema.Scope);

                                if (syncJob.Seed)
                                {
                                    SeedTable(targetConn, tableSchema, sourceConn, scope);
                                }
                                else if (tableSchema.SourceVersion.Equals(tableSchema.TargetVersion))
                                {
                                    Logger.LogInformation("{Scope} Already up to date", scope);
                                }
                                else
                                {
                                    SyncTable(targetConn, tableSchema, sourceConn, scope);
                                }

                                syncStopWatch.Stop();
                                Logger.LogInformation("{Scope} End {TableSchemaScope}, duration {Elapsed}", scope, tableSchema.Scope, syncStopWatch.Elapsed);
                                targetConn.PersistsSourceTargetVersionState(tableSchema);
                            }
                        }
                        catch (Exception ex)
                        {
                            syncStopWatch.Stop();
                            Logger.LogError(
                                ex,
                                "{Scope} Exception {TableSchemaScope}, duration {Elapsed}, exception: {Message}",
                                scope,
                                tableSchema.Scope,
                                syncStopWatch.Elapsed,
                                ex.Message
                                );

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
            targetConn.TruncateTargetTable(tableSchema, scope, Logger);
            sourceConn.BulkCopyDataDirect(targetConn, tableSchema, scope, Logger);
        }

        private void SyncTable(SqlConnection targetConn, TableSchema tableSchema, SqlConnection sourceConn, object scope)
        {
            if (targetConn.SyncTablesExist(tableSchema))
            {
                throw new Exception($"{scope} Aborting! Sync tables already exists ({tableSchema.SyncNewOrUpdatedTableName}, {tableSchema.SyncDeletedTableName})");
            }
            try
            {
                targetConn.CreateSyncTables(tableSchema, scope, Logger);
                sourceConn.BulkCopyData(targetConn, tableSchema, scope, Logger);
                targetConn.DeleteData(tableSchema, scope, Logger);
                targetConn.MergeData(tableSchema, scope, Logger);
            }
            finally
            {
                targetConn.DropSyncTables(tableSchema, scope, Logger);
            }
        }
    }
}
