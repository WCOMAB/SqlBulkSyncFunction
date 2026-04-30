using System;

namespace SqlBulkSyncFunction.Models.Schema.Export;

#nullable enable

/// <summary>
/// Outcome of attempting to create a schema tracking export job.
/// </summary>
/// <param name="Code">Whether creation succeeded or why it was rejected.</param>
/// <param name="Job">Populated when <paramref name="Code"/> is <see cref="ExportJobCreateResultCode.Created"/>.</param>
public record ExportJobCreateResult(ExportJobCreateResultCode Code, SchemaTrackingExportJob? Job);

/// <summary>
/// Result codes for <see cref="ExportJobCreateResult"/>.
/// </summary>
public enum ExportJobCreateResultCode
{
    /// <summary>
    /// Job was persisted and enqueued.
    /// </summary>
    Created = 0,

    /// <summary>
    /// Request body failed validation (missing or whitespace fields).
    /// </summary>
    ValidationFailed = 1,

    /// <summary>
    /// Job configuration or table mapping was not found for the route.
    /// </summary>
    NotFound = 2
}
