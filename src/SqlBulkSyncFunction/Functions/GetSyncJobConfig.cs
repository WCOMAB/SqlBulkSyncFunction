using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Models.Schema;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions;

public partial class GetSyncJobConfig(
    ILogger<GetSyncJobConfig> logger,
    IOptions<SyncJobsConfig> syncJobsConfig,
    ITokenCacheService tokenCacheService
    )
{

    [Function(nameof(GetSyncJobConfig) + nameof(ListAreas))]
    public IActionResult ListAreas(
       [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route ="config"
        )] HttpRequest req
       )
    {
        ArgumentNullException.ThrowIfNull(req);

        return syncJobsConfig?.Value?.Jobs?.Values
            ?.Where(job => job.Area is { Length: > 0 })
            .Select(job => job.Area)
            .Distinct()
            .ToArray() is { Length: >0 } areas 
                ? new OkObjectResult(areas)
                : new NoContentResult();
    }

    [Function(nameof(GetSyncJobConfig) + nameof(ListIds))]
    public IActionResult ListIds(
       [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route ="config/{area}"
        )] HttpRequest req,
       string area
       )
    {
        ArgumentNullException.ThrowIfNull(req);

        return
            !string.IsNullOrWhiteSpace(area) &&
            syncJobsConfig?.Value?.Jobs
                ?.Where(job => job.Value.Area == area)
                .Select(job => job.Key)
                .ToArray() is { Length: >0 } ids 
                ? new OkObjectResult(ids)
                : new NoContentResult();
    }

    [Function(nameof(GetSyncJobConfig) + nameof(GetJobConfig))]
    public IActionResult GetJobConfig(
       [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route ="config/{area}/{id}"
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
            var tables = jobConfig.Tables.ToDictionary(
                key => key.Key,
                value => new SyncJobConfigTableDto(
                        Source: value.Value,
                        Target: jobConfig.TargetTables.TryGetValue(value.Key, out var target) && !string.IsNullOrWhiteSpace(target) ? target : value.Value,
                        DisableTargetIdentityInsert: jobConfig.DisableTargetIdentityInsertTables.TryGetValue(value.Key, out var disableTargetIdentityInsert) && disableTargetIdentityInsert
                    )
                );
            return new OkObjectResult(
                new SyncJobConfigResponse(
                    Id: id,
                    Area: area,
                    BatchSize: jobConfig.BatchSize,
                    Manual: jobConfig.Manual,
                    Schedules: jobConfig.Schedules,
                    Tables: tables
                )
            );
        }
        return new NotFoundResult();
    }

    [Function(nameof(GetSyncJobConfig) + nameof(GetJobConfigSchema))]
    public async Task<IActionResult> GetJobConfigSchema(
      [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route ="config/{area}/{id}/schema"
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
            var syncJob = jobConfig.ToSyncJob(
                    tokenCache: await tokenCacheService.GetTokenCache(jobConfig),
                    expires: DateTimeOffset.UtcNow.AddMinutes(4),
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
