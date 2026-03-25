using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Functions;

/// <summary>
/// Timer that drains schedule and progress queues in order and refreshes per-job aggregate blobs under the monitor container.
/// </summary>
public sealed class AggregateSyncMonitoring(SyncMonitoringAggregationService aggregationService)
{
    /// <summary>
    /// Runs every minute on the minute (UTC).
    /// </summary>
    [Function(nameof(AggregateSyncMonitoring))]
    public Task Run(
#pragma warning disable IDE0060 // Remove unused parameter
        [TimerTrigger("0 */1 * * * *")] TimerInfo timerInfo,
#pragma warning restore IDE0060 // Remove unused parameter
        CancellationToken cancellationToken
    )
        => aggregationService.ProcessAllQueuesAsync(cancellationToken);
}
