using System;
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
            using var scope = Logger.BeginScope(syncJob.Id);
            await using SqlConnection
                sourceConn = new(syncJob.SourceDbConnection) { AccessToken = syncJob.SourceDbAccessToken },
                targetConn = new(syncJob.TargetDbConnection) { AccessToken = syncJob.TargetDbAccessToken };

            using IDisposable from = Logger.BeginScope($"{sourceConn.DataSource}.{sourceConn.Database}"),
                                to = Logger.BeginScope($"{targetConn.DataSource}.{targetConn.Database}");

            Logger.LogInformation(
                "Connecting to source database {0}.{1}",
                sourceConn.DataSource,
                sourceConn.Database
            );
            await sourceConn.OpenAsync();
            Logger.LogInformation("Connected {0}", sourceConn.ClientConnectionId);

            Logger.LogInformation(
                "Connecting to target database {0}.{1}",
                targetConn.DataSource,
                targetConn.Database
            );
            await targetConn.OpenAsync();
            Logger.LogInformation("Connected {0}", targetConn.ClientConnectionId);

            Logger.LogInformation("Ensuring sync schema and table exists...");
            targetConn.EnsureSyncSchemaAndTableExists(Logger);
            Logger.LogInformation("Ensured sync schema and table exist");

            Logger.LogInformation("Fetching table schemas...");
            var schemaStopWatch = Stopwatch.StartNew();
            var tableSchemas = (
                    syncJob.Tables
                    ?? Array.Empty<string>()
                )
                .Select(
                    table => TableSchema.LoadSchema(sourceConn, targetConn, table, syncJob.BatchSize, globalChangeTracking)
                ).ToArray();
            schemaStopWatch.Stop();
            Logger.LogInformation("Found {0} tables, duration {1}", tableSchemas.Length, schemaStopWatch.Elapsed);

            Array.ForEach(
                tableSchemas,
                tableSchema => {
                    using (Logger.BeginScope(tableSchema.TableName))
                    {
                        Logger.LogInformation("Begin {0}", tableSchema.TableName);
                        var syncStopWatch = Stopwatch.StartNew();
                        if (tableSchema.SourceVersion.Equals(tableSchema.TargetVersion))
                        {
                            Logger.LogInformation("Already up to date");
                        }
                        else
                        {
                            SyncTable(targetConn, tableSchema, sourceConn);
                        }
                        syncStopWatch.Stop();
                        Logger.LogInformation("End {0}, duration {1}", tableSchema.TableName, syncStopWatch.Elapsed);
                        targetConn.PersistsSourceTargetVersionState(tableSchema);
                    }
                }
            );
        }

        private void SyncTable(SqlConnection targetConn, TableSchema tableSchema, SqlConnection sourceConn)
        {
            try
            {
                targetConn.CreateSyncTables(tableSchema, Logger);
                sourceConn.BulkCopyData(targetConn, tableSchema, Logger);
                targetConn.DeleteData(tableSchema, Logger);
                targetConn.MergeData(tableSchema, Logger);
            }
            finally
            {
                targetConn.DropSyncTables(tableSchema, Logger);
            }
        }
    }
}
