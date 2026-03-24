using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace SqlBulkSyncFunction.Functions;

public partial class GetSyncJobConfig
{
    [Function(nameof(GetSyncJobConfig) + nameof(ListIds))]
    public IActionResult ListIds(
       [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route = "config/{area}"
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
                .ToArray() is { Length: > 0 } ids
                ? new OkObjectResult(ids)
                : new NoContentResult();
    }
}
