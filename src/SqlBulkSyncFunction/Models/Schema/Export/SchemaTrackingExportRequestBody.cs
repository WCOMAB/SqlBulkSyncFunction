namespace SqlBulkSyncFunction.Models.Schema.Export;

#nullable enable

/// <summary>
/// Request body accepted by <c>POST .../schema/tracking/{tableId}/export</c> to justify and trace a sensitive data export.
/// </summary>
/// <param name="Author">Person or system initiating the export (required, non-whitespace).</param>
/// <param name="ReferenceId">External ticket or reference identifier (required, non-whitespace).</param>
/// <param name="Purpose">Business justification for exporting table data (required, non-whitespace).</param>
public record SchemaTrackingExportRequestBody(
    string Author,
    string ReferenceId,
    string Purpose
);
