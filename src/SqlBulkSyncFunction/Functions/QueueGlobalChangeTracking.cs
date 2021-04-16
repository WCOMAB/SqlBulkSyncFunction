using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

namespace  SqlBulkSyncFunction.Functions
{
    public record QueueGlobalChangeTracking(
        ILogger<QueueGlobalChangeTracking> Logger,
        IOptions<SyncJobsConfig> SyncJobsConfig,
        ITokenCacheService TokenCacheService
        )
    {
        [Function(nameof(QueueGlobalChangeTracking))]
        public async Task<QueueGlobalChangeTrackingResult> Run(
            [HttpTrigger(
                AuthorizationLevel.Function,
                "post",
                Route ="queue/{area}/{id}"
            )] HttpRequestData req,
            string area,
            string id
            )
        {
            if (string.IsNullOrWhiteSpace(area) ||
                string.IsNullOrWhiteSpace(id) ||
                !SyncJobsConfig.Value.Jobs.TryGetValue(id, out var jobConfig) ||
                !StringComparer.OrdinalIgnoreCase.Equals(area, jobConfig?.Area))
            {
                return new QueueGlobalChangeTrackingResult(
                    null,
                    req.CreateResponse(HttpStatusCode.NotFound)
                );
            }

            return new QueueGlobalChangeTrackingResult(
                jobConfig.ToSyncJob(
                    tokenCache: await TokenCacheService.GetTokenCache(jobConfig),
                    expires: DateTimeOffset.UtcNow.AddMinutes(4),
                    id: id
                ),
                req.CreateResponse(HttpStatusCode.Accepted)
            );
        }
    }
}
