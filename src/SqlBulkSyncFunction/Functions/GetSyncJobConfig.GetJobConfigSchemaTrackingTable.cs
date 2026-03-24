using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Returns change tracking primary key details for a specific configured table.
    /// </summary>
    [Function(nameof(GetSyncJobConfig) + nameof(GetJobConfigSchemaTrackingTable))]
    public async Task<IActionResult> GetJobConfigSchemaTrackingTable(
      [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route = "config/{area}/{id}/schema/tracking/{tableId}"
        )] HttpRequest req,
      string area,
      string id,
      string tableId
      )
    {
        ArgumentNullException.ThrowIfNull(req);

        if (!string.IsNullOrWhiteSpace(area) &&
            !string.IsNullOrWhiteSpace(id) &&
            !string.IsNullOrWhiteSpace(tableId) &&
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

            var table = (syncJob.Tables ?? [])
                .FirstOrDefault(configuredTable => StringComparer.OrdinalIgnoreCase.Equals(configuredTable.Id, tableId));

            if (table == null)
            {
                return new NotFoundResult();
            }

            await using SqlConnection
                sourceConn = new(syncJob.SourceDbConnection) { AccessToken = syncJob.SourceDbAccessToken },
                targetConn = new(syncJob.TargetDbConnection) { AccessToken = syncJob.TargetDbAccessToken };

            using IDisposable
                from = logger.BeginScope("{DataSource}.{Database}", sourceConn.DataSource, sourceConn.Database),
                to = logger.BeginScope("{DataSource}.{Database}", targetConn.DataSource, targetConn.Database);

            await sourceConn.OpenAsync();
            await targetConn.OpenAsync();

            targetConn.EnsureSyncSchemaAndTableExists($"config/{id}/{area}/schema/tracking/{tableId}", logger);

            var columns = sourceConn.GetColumns(table.Source);
            var sourceVersion = sourceConn.GetSourceVersion(table.Source, globalChangeTracking: true, columns);
            var targetVersion = targetConn.GetTargetVersion(table.Target);
            var fromVersion = targetVersion.CurrentVersion < 0 ? 0L : targetVersion.CurrentVersion;

            if (sourceVersion == null)
            {
                return new OkObjectResult(
                    new SchemaTrackingPrimaryKeyDetailsResponse([], [], [])
                );
            }

            var changedRows = await sourceConn.QueryAsync(
                SqlStatementExtensions.GetChangeTrackingPrimaryKeyDetailsSelectStatement(table.Source, columns),
                new { FromVersion = fromVersion }
            );

            var updated = new List<IReadOnlyDictionary<string, object>>();
            var inserted = new List<IReadOnlyDictionary<string, object>>();
            var deleted = new List<IReadOnlyDictionary<string, object>>();

            foreach (var row in changedRows)
            {
                if (row is not IDictionary<string, object> values ||
                    !values.TryGetValue("Operation", out var operationValue) ||
                    operationValue is not string operation ||
                    string.IsNullOrWhiteSpace(operation))
                {
                    continue;
                }

                var primaryKeyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var value in values)
                {
                    if (!string.Equals(value.Key, "Operation", StringComparison.OrdinalIgnoreCase))
                    {
                        primaryKeyValues[value.Key] = value.Value;
                    }
                }

                switch (operation)
                {
                    case "U":
                        updated.Add(primaryKeyValues);
                        break;
                    case "I":
                        inserted.Add(primaryKeyValues);
                        break;
                    case "D":
                        deleted.Add(primaryKeyValues);
                        break;
                }
            }

            return new OkObjectResult(
                new SchemaTrackingPrimaryKeyDetailsResponse(updated, inserted, deleted)
            );
        }

        return new NotFoundResult();
    }
}
