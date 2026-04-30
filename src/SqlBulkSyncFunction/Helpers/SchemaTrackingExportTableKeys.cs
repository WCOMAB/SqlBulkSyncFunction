using System.Globalization;
using SqlBulkSyncFunction.Services;

namespace SqlBulkSyncFunction.Helpers;

/// <summary>
/// Builds Azure Table Storage partition keys for export job entities.
/// </summary>
public static class SchemaTrackingExportTableKeys
{
    /// <summary>
    /// Builds partition key <c>{area}_{id}_{tableId}</c> with per-segment sanitization compatible with Azure Table key rules.
    /// </summary>
    /// <param name="area">Job area segment.</param>
    /// <param name="jobId">Job configuration id segment.</param>
    /// <param name="tableId">Table mapping id segment.</param>
    /// <returns>Composite partition key string.</returns>
    public static string GetPartitionKey(string area, string jobId, string tableId)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}_{1}_{2}",
            SyncMonitoringAggregationService.SanitizeBlobPathSegment(area),
            SyncMonitoringAggregationService.SanitizeBlobPathSegment(jobId),
            SyncMonitoringAggregationService.SanitizeBlobPathSegment(tableId));
}
