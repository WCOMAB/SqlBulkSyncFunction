using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Functions;

public partial class GetSyncJobConfig
{
    [Function(nameof(GetSyncJobConfig) + nameof(GetJobConfig))]
    public IActionResult GetJobConfig(
       [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route = "config/{area}/{id}"
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
            var tables = jobConfig.Tables?.ToDictionary(
                key => key.Key,
                value => new SyncJobConfigTableDto(
                        Source: value.Value,
                        Target: jobConfig.TargetTables?.TryGetValue(value.Key, out var target) == true && !string.IsNullOrWhiteSpace(target) ? target : value.Value,
                        DisableTargetIdentityInsert: jobConfig.DisableTargetIdentityInsertTables.GetValueOrDefault(value.Key),
                        DisableConstraintCheck: jobConfig.DisableConstraintCheckTables.GetValueOrDefault(value.Key)
                    )
                ) ?? [];
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
}
