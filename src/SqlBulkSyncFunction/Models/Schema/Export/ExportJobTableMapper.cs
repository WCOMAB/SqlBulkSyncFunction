namespace SqlBulkSyncFunction.Models.Schema.Export;

#nullable enable

/// <summary>
/// Maps <see cref="ExportJobTableEntity"/> rows to API DTOs shared by list and single-job status endpoints.
/// </summary>
public static class ExportJobTableMapper
{
    /// <summary>
    /// Maps a table row to <see cref="SchemaTrackingExportJobListItem"/>; <paramref name="result"/> is set only when loading <c>result.json</c> for detail responses.
    /// </summary>
    /// <param name="entity">Persisted export job row.</param>
    /// <param name="result">Optional SAS ZIP URIs for status detail when <c>result.json</c> was loaded.</param>
    public static SchemaTrackingExportJobListItem ToListItem(ExportJobTableEntity entity, SchemaTrackingExportJobListItemResult? result = null)
        => new(
            CorrelationId: entity.CorrelationId,
            ExportJobId: entity.RowKey,
            Status: ParseStatus(entity.Status),
            ReferenceId: entity.ReferenceId ?? string.Empty,
            Created: entity.CreatedUtc,
            Completed: entity.CompletedUtc,
            UpdatedDone: entity.UpdatedDone,
            InsertedDone: entity.InsertedDone,
            DeletedDone: entity.DeletedDone,
            Error: entity.Error,
            Result: result
        );

    /// <summary>
    /// Parses the persisted status string from table storage into <see cref="SchemaTrackingExportJobStatus"/>.
    /// </summary>
    /// <param name="status">Raw <see cref="ExportJobTableEntity.Status"/> value.</param>
    public static SchemaTrackingExportJobStatus ParseStatus(string? status)
        => status switch
        {
            nameof(SchemaTrackingExportJobStatus.Running) => SchemaTrackingExportJobStatus.Running,
            nameof(SchemaTrackingExportJobStatus.Completed) => SchemaTrackingExportJobStatus.Completed,
            nameof(SchemaTrackingExportJobStatus.Failed) => SchemaTrackingExportJobStatus.Failed,
            _ => SchemaTrackingExportJobStatus.Pending
        };
}
