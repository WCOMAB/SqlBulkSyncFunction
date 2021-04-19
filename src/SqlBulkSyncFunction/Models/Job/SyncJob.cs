using System;
using System.Collections.Generic;

namespace SqlBulkSyncFunction.Models.Job
{
    public record SyncJob(
        string Id,
        string Area,
        DateTimeOffset Expires,
        string SourceDbConnection,
        string TargetDbConnection,
        ICollection<SyncJobTable> Tables,
        int? BatchSize,
        string SourceDbAccessToken = null,
        string TargetDbAccessToken = null
    );
}
