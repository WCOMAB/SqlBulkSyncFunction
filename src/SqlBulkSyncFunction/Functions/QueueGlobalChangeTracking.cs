using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using SqlBulkSyncFunction.Helpers;
using SqlBulkSyncFunction.Models;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions;

public partial class QueueGlobalChangeTracking(
    IOptions<SyncJobsConfig> syncJobsConfig,
    ITokenCacheService tokenCacheService,
    IProcessSyncJobService processSyncJobService,
    SyncProgressService syncProgressService
    )
{
    [Function(nameof(QueueGlobalChangeTracking) + nameof(Queue))]
    public async Task<IActionResult> Queue(
        [HttpTrigger(
            AuthorizationLevel.Function,
            "post",
            Route ="queue/{area}/{id}"
        )] HttpRequest req,
        string area,
        string id,
        CancellationToken cancellationToken
        ) => await GetQueueGlobalChangeTrackingResult(req, area, id, false, cancellationToken);

    [Function(nameof(QueueGlobalChangeTracking) + nameof(Seed))]
    public async Task<IActionResult> Seed(
        [HttpTrigger(
            AuthorizationLevel.Function,
            "post",
            Route ="queue/{area}/{id}/{seed}"
        )] HttpRequest req,
        string area,
        string id,
        bool seed,
        CancellationToken cancellationToken
    ) => await GetQueueGlobalChangeTrackingResult(req, area, id, seed, cancellationToken);

    private async Task<IActionResult> GetQueueGlobalChangeTrackingResult(
#pragma warning disable IDE0060 // Remove unused parameter
        HttpRequest req,
#pragma warning restore IDE0060 // Remove unused parameter
        string area,
        string id,
        bool seed,
        CancellationToken cancellationToken
        )
    {

        if (string.IsNullOrWhiteSpace(area) ||
            string.IsNullOrWhiteSpace(id) ||
            !syncJobsConfig.Value.Jobs.TryGetValue(id, out var jobConfig) ||
            !StringComparer.OrdinalIgnoreCase.Equals(area, jobConfig?.Area))
        {
            return new NotFoundResult();
        }

        LogSchedule logSchedule = new(
                                         nameof(SyncJobConfig.Manual),
                                         DateTimeOffset.UtcNow,
                                         DateTimeOffset.UtcNow.AddMinutes(4),
                                         false,
                                         null,
                                         null,
                                         null
                                     );

        var syncJob = jobConfig.ToSyncJob(
                logSchedule.CorrelationId,
                tokenCache: await tokenCacheService.GetTokenCache(jobConfig),
                timestamp: logSchedule.Timestamp,
                expires: logSchedule.Expires,
                id: id,
                schedule: nameof(jobConfig.Manual),
                seed: seed
            );

        var result = logSchedule with
        {
            SyncJobs = [syncJob.ToLogSyncJob()]
        };

        var createdProgress = new SyncJobProgress(
                        Area: syncJob.Area,
                        ConfigurationId: syncJob.Id,
                        Schedule: syncJob.Schedule,
                        ScheduleCorrelationId: syncJob.ScheduleCorrelationId,
                        SyncJobCorrelationId: syncJob.CorrelationId,
                        State: SyncJobProgressState.Created
                    );

        await syncProgressService.Report(createdProgress, cancellationToken);

        await syncProgressService.Report(
                 result,
                 cancellationToken
             );

        await processSyncJobService.EnqueueSyncJob(syncJob, cancellationToken);

        // TODO: consider returning a URL to check the status of the job
        return new AcceptedResult(
            location: null,
            value: result);
    }
}
