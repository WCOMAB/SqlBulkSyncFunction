using System;
using System.Linq;
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
    [Function(nameof(GetSyncJobConfig) + nameof(GetJobConfigSchema))]
    public async Task<IActionResult> GetJobConfigSchema(
      [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route = "config/{area}/{id}/schema"
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

            DbInfo
                sourceDbInfo = await sourceConn.QueryFirstAsync<DbInfo>(SchemaExtensions.DbInfoQuery),
                targetDbInfo = await targetConn.QueryFirstAsync<DbInfo>(SchemaExtensions.DbInfoQuery);

            SourceTableChangeTrackingInfo[]
                sourceTableChangeTrackingInfos = [.. await sourceConn.QueryAsync<SourceTableChangeTrackingInfo>(SchemaExtensions.SourceTableChangeTrackingInfoQuery)];

            targetConn.EnsureSyncSchemaAndTableExists($"config/{id}/{area}/schema", logger);

            var tableSchemas = (
                    syncJob.Tables ?? []
                )
                .Select(
                    table => TableSchema.LoadSchema(
                        sourceConn,
                        targetConn,
                        table,
                        syncJob.BatchSize,
                        globalChangeTracking: true
                        )
                )
                .Select(table => new
                {
                    table.SourceTableName,
                    table.TargetTableName,
                    table.SourceVersion,
                    table.TargetVersion,
                    table.Columns
                })
                .ToArray();

            return new OkObjectResult(
                new
                {
                    sourceDbInfo,
                    targetDbInfo,
                    tableSchemas,
                    sourceTableChangeTrackingInfos
                }
                );
        }
        return new NotFoundResult();
    }
}
