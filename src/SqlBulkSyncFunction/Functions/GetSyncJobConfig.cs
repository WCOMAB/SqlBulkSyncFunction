using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions;

public partial class GetSyncJobConfig(
    ILogger<GetSyncJobConfig> logger,
    IOptions<SyncJobsConfig> syncJobsConfig,
    ITokenCacheService tokenCacheService,
    SchemaTrackingExportService schemaTrackingExportService
    )
{
}
