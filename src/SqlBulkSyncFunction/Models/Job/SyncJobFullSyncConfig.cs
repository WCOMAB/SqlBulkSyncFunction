namespace SqlBulkSyncFunction.Models.Job;

/// <summary>
/// Job-level opt-in for interval full sync (no change tracking).
/// Presence of this config on a job enables the mode for all tables in the job.
/// </summary>
public record SyncJobFullSyncConfig
{
    /// <summary>Minimum milliseconds between syncs for a table.</summary>
    public long IntervalMilliseconds { get; init; }

    /// <summary>When true, full-sync source reads use Snapshot isolation (no NOLOCK).</summary>
    public bool UseSnapshotIsolation { get; init; }

    /// <summary>When true, full-sync source bulk reads use ApplicationIntent=ReadOnly.</summary>
    public bool UseApplicationIntentReadOnly { get; init; }
}
