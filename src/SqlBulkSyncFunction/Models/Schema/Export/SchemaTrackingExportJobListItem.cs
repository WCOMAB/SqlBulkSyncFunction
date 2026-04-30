using System;

namespace SqlBulkSyncFunction.Models.Schema.Export;

#nullable enable

/// <summary>
/// Export job row returned by <c>GET .../export/status</c> (list) and <c>GET .../export/status/{correlationId}</c> (detail); <see cref="Result"/> is set only on detail when finalized.
/// </summary>
/// <param name="CorrelationId">Full correlation path used for blobs and deep links.</param>
/// <param name="ExportJobId">Export job id (<c>n</c>-format version 7 GUID); same as Azure Table row key.</param>
/// <param name="Status">Current job status.</param>
/// <param name="ReferenceId">Reference from the export request.</param>
/// <param name="Created">UTC creation time.</param>
/// <param name="Completed">UTC completion time when finished; <see langword="null"/> if not completed.</param>
/// <param name="UpdatedDone">Whether the updated ZIP leg completed.</param>
/// <param name="InsertedDone">Whether the inserted ZIP leg completed.</param>
/// <param name="DeletedDone">Whether the deleted ZIP leg completed.</param>
/// <param name="Error">Optional error message when status is failed.</param>
/// <param name="Result">Populated for single-job status when <c>response/result.json</c> exists; otherwise <see langword="null"/>.</param>
public record SchemaTrackingExportJobListItem(
    string CorrelationId,
    string ExportJobId,
    SchemaTrackingExportJobStatus Status,
    string ReferenceId,
    DateTimeOffset Created,
    DateTimeOffset? Completed,
    bool UpdatedDone,
    bool InsertedDone,
    bool DeletedDone,
    string? Error,
    SchemaTrackingExportJobListItemResult? Result = null
);
