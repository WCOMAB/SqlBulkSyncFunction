using System;

namespace SqlBulkSyncFunction.Models;

public record LogSchedule(
    string Name,
    DateTimeOffset Timestamp,
    DateTimeOffset Expires,
    bool IsPastDue,
    DateTimeOffset? Last,
    DateTimeOffset? Next,
    DateTimeOffset? LastUpdated
    )
{
    public string CorrelationId { get; init; } = FormattableString.Invariant($"{Name}/{Timestamp.Year:0000}/{Timestamp.Month:00}/{Timestamp.Day:00}/{Timestamp.Hour:00}/{Timestamp.Minute:00}{Guid.CreateVersion7():n}");
    public LogSyncJob[] SyncJobs { get; init; } = [];
}


public record LogSyncJob(
    string CorrelationId,
    string Id,
    string Area,
    bool Seed
    );
