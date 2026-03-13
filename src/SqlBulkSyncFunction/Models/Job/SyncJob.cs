using System;
using System.Collections.Generic;

namespace SqlBulkSyncFunction.Models.Job;

public record SyncJob(
    string ScheduleCorrelationId,
    string Id,
    string Schedule,
    string Area,
    DateTimeOffset Timestamp,
    DateTimeOffset Expires,
    string SourceDbConnection,
    string TargetDbConnection,
    ICollection<SyncJobTable> Tables,
    int? BatchSize,
    bool Seed,
    string SourceDbAccessToken = null,
    string TargetDbAccessToken = null
)
{
    public string CorrelationId { get; init; } = FormattableString.Invariant($"{Schedule}/{Area}/{Id}/{Timestamp.Year:0000}/{Timestamp.Month:00}/{Timestamp.Day:00}/{Timestamp.Hour:00}/{Timestamp.Minute:00}{Guid.CreateVersion7():n}");
}
