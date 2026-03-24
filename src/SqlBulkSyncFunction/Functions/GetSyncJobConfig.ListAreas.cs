using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace SqlBulkSyncFunction.Functions;

public partial class GetSyncJobConfig
{
    [Function(nameof(GetSyncJobConfig) + nameof(ListAreas))]
    public IActionResult ListAreas(
       [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route = "config"
        )] HttpRequest req
       )
    {
        ArgumentNullException.ThrowIfNull(req);

        return syncJobsConfig?.Value?.Jobs?.Values
            ?.Where(job => job.Area is { Length: > 0 })
            .Select(job => job.Area)
            .Distinct()
            .ToArray() is { Length: > 0 } areas
                ? new OkObjectResult(areas)
                : new NoContentResult();
    }
}
