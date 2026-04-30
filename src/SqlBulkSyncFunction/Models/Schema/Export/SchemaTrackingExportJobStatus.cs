using System.Text.Json.Serialization;

namespace SqlBulkSyncFunction.Models.Schema.Export;

#nullable enable

/// <summary>
/// High-level lifecycle state for a schema tracking export job persisted in Azure Table Storage.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SchemaTrackingExportJobStatus
{
    /// <summary>
    /// Job was accepted; work may not have started yet.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Dispatcher has enqueued segment workers or segment work is in progress.
    /// </summary>
    Running = 1,

    /// <summary>
    /// All segments completed and result metadata was written.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// A fatal error occurred during export or finalize.
    /// </summary>
    Failed = 3
}
