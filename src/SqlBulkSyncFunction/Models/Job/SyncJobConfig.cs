using System.Collections.Generic;

namespace SqlBulkSyncFunction.Models.Job;

public record SyncJobConfig
{
    public SyncJobConfigDataSource Source { get; init; }
    public SyncJobConfigDataSource Target { get; init; }
    public Dictionary<string, string> Tables { get; init; } = [];
    public Dictionary<string, string> TargetTables { get; init; } = [];
    public Dictionary<string, bool> DisableTargetIdentityInsertTables { get; init; } = [];
    public Dictionary<string, bool> DisableConstraintCheckTables { get; init; } = [];
    public Dictionary<string, bool> DeleteInsteadOfTruncateTables { get; init; } = [];
    public Dictionary<string, bool> ReseedTargetIdentityAfterClearTables { get; init; } = [];
    public int? BatchSize { get; init; }
    public bool UseSnapshotIsolationSeed { get; init; }
    public bool UseApplicationIntentReadOnlySeed { get; init; }
    /// <summary>When non-null, job uses interval full sync instead of change tracking.</summary>
    public SyncJobFullSyncConfig FullSync { get; init; }
    public string Area { get; init; }
    public bool? Manual { get; init; }
    public Dictionary<string, bool> Schedules { get; init; } = [];
}
