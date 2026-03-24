using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models.Schema;

namespace SqlBulkSyncFunction.Functions;

public partial class GetSyncJobConfig
{
    [Function(nameof(GetSyncJobConfig) + nameof(GetJobConfigSchemaTracking))]
    public async Task<IActionResult> GetJobConfigSchemaTracking(
      [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route = "config/{area}/{id}/schema/tracking"
        )] HttpRequest req,
      string area,
      string id
      )
    {
        ArgumentNullException.ThrowIfNull(req);

        if (!string.IsNullOrWhiteSpace(area) &&
            !string.IsNullOrWhiteSpace(id) &&
            syncJobsConfig?.Value?.Jobs?.TryGetValue(id, out var jobConfig) == true &&
            StringComparer.OrdinalIgnoreCase.Equals(area, jobConfig?.Area))
        {
            var utcNow = DateTimeOffset.UtcNow;
            var syncJob = jobConfig.ToSyncJob(
                    null,
                    tokenCache: await tokenCacheService.GetTokenCache(jobConfig),
                    timestamp: utcNow,
                    expires: utcNow.AddMinutes(4),
                    id: id,
                    schedule: nameof(jobConfig.Manual),
                    seed: false
                );

            await using SqlConnection
                sourceConn = new(syncJob.SourceDbConnection) { AccessToken = syncJob.SourceDbAccessToken },
                targetConn = new(syncJob.TargetDbConnection) { AccessToken = syncJob.TargetDbAccessToken };

            using IDisposable
                from = logger.BeginScope("{DataSource}.{Database}", sourceConn.DataSource, sourceConn.Database),
                to = logger.BeginScope("{DataSource}.{Database}", targetConn.DataSource, targetConn.Database);

            await sourceConn.OpenAsync();
            await targetConn.OpenAsync();

            targetConn.EnsureSyncSchemaAndTableExists($"config/{id}/{area}/schema/tracking", logger);

            var rows = new List<SchemaTrackingTableRow>();
            foreach (var table in syncJob.Tables ?? [])
            {
                var columns = sourceConn.GetColumns(table.Source);
                var sourceVersion = sourceConn.GetSourceVersion(table.Source, globalChangeTracking: true, columns);
                var targetVersion = targetConn.GetTargetVersion(table.Target);
                var fromVersion = targetVersion.CurrentVersion < 0 ? 0L : targetVersion.CurrentVersion;

                if (sourceVersion == null)
                {
                    rows.Add(
                        new SchemaTrackingTableRow(
                            table.Id,
                            table.Source,
                            0,
                            0,
                            0,
                            -1,
                            targetVersion.CurrentVersion,
                            table.Target
                        )
                    );
                    continue;
                }

                var counts = await sourceConn.QueryFirstAsync<ChangeOperationCounts>(
                    SqlStatementExtensions.GetChangeTrackingOperationCountsSelectStatement(table.Source),
                    new { FromVersion = fromVersion }
                    );

                rows.Add(
                    new SchemaTrackingTableRow(
                        table.Id,
                        table.Source,
                        counts.Updated,
                        counts.Inserted,
                        counts.Deleted,
                        sourceVersion.CurrentVersion,
                        targetVersion.CurrentVersion,
                        table.Target
                    )
                );
            }

            return new OkObjectResult(rows);
        }
        return new NotFoundResult();
    }
}
