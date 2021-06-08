using System;
using System.Collections.Generic;

namespace SqlBulkSyncFunction.Models.Job
{
    public record SyncJob(
        string Id,
        string Schedule,
        string Area,
        DateTimeOffset Expires,
        string SourceDbConnection,
        string TargetDbConnection,
        ICollection<SyncJobTable> Tables,
        int? BatchSize,
        bool Seed,
        string SourceDbAccessToken = null,
        string TargetDbAccessToken = null
    );
}
