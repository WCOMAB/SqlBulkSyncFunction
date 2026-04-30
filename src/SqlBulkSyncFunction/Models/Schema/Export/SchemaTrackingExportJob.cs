using System;

namespace SqlBulkSyncFunction.Models.Schema.Export;

#nullable enable

/// <summary>
/// Metadata for an accepted schema tracking export job, stored as <c>job.json</c> under the export blob prefix.
/// </summary>
/// <param name="Area">Configured sync job area.</param>
/// <param name="Id">Configured sync job identifier.</param>
/// <param name="TableId">Configured table mapping identifier.</param>
/// <param name="CorrelationId">Blob path prefix segments for this job (date/hour/minute + version 7 id).</param>
/// <param name="ExportJobId">Version 7 GUID in <c>n</c> format (no dashes); same value as Azure Table row key.</param>
/// <param name="ReferenceId">Reference from the export request.</param>
/// <param name="Author">Author from the export request.</param>
/// <param name="Purpose">Purpose from the export request.</param>
/// <param name="Created">UTC creation time when the job was accepted.</param>
public record SchemaTrackingExportJob(
    string Area,
    string Id,
    string TableId,
    string CorrelationId,
    string ExportJobId,
    string ReferenceId,
    string Author,
    string Purpose,
    DateTimeOffset Created
);
