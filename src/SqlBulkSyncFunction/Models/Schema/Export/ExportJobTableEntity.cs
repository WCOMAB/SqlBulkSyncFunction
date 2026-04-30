using System;
using Azure.Data.Tables;

namespace SqlBulkSyncFunction.Models.Schema.Export;

#nullable enable

/// <summary>
/// Azure Table Storage entity for schema tracking export job coordination.
/// Partition key: <c>area_id_tableId</c> (sanitized segments). Row key: export job id (version 7 GUID, <c>n</c> format, no dashes).
/// </summary>
public sealed class ExportJobTableEntity : ITableEntity
{
    /// <inheritdoc />
    public string PartitionKey { get; set; } = string.Empty;

    /// <inheritdoc />
    public string RowKey { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? Timestamp { get; set; }

    /// <inheritdoc />
    public Azure.ETag ETag { get; set; }

    /// <summary>
    /// Full correlation id path used as blob prefix and in queue messages.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Sync job area (configuration).
    /// </summary>
    public string Area { get; set; } = string.Empty;

    /// <summary>
    /// Sync job configuration id.
    /// </summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>
    /// Configured table mapping id.
    /// </summary>
    public string TableId { get; set; } = string.Empty;

    /// <summary>
    /// Reference id from the export request (denormalized for list queries without reading <c>job.json</c>).
    /// </summary>
    public string ReferenceId { get; set; } = string.Empty;

    /// <summary>
    /// Serialized <see cref="SchemaTrackingExportJobStatus"/> value name.
    /// </summary>
    public string Status { get; set; } = nameof(SchemaTrackingExportJobStatus.Pending);

    /// <summary>
    /// Whether the updated ZIP segment finished successfully.
    /// </summary>
    public bool UpdatedDone { get; set; }

    /// <summary>
    /// Whether the inserted ZIP segment finished successfully.
    /// </summary>
    public bool InsertedDone { get; set; }

    /// <summary>
    /// Whether the deleted ZIP segment finished successfully.
    /// </summary>
    public bool DeletedDone { get; set; }

    /// <summary>
    /// UTC time when the job was accepted.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>
    /// UTC time when the job completed successfully; null if not completed.
    /// </summary>
    public DateTimeOffset? CompletedUtc { get; set; }

    /// <summary>
    /// Optional failure message.
    /// </summary>
    public string? Error { get; set; }
}
