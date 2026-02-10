namespace SqlBulkSyncFunction.Models.Job;

public record SyncJobTable(
    string Source,
    string Target,
    bool DisableTargetIdentityInsert
    );
