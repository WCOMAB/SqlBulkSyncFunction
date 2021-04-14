using System;
using System.Collections.Generic;

namespace SqlBulkSyncFunction.Models.Job
{
    public record SyncJob(
        DateTimeOffset Expires,
        string SourceDbConnection,
        string TargetDbConnection,
        ICollection<string> Tables,
        int? BatchSize,
        string SourceDbAccessToken = null,
        string TargetDbAccessToken = null
    );
}
