using System;
using System.Collections.Generic;

namespace SqlBulkSyncFunction.Models.Monitor;

/// <summary>
/// Progress steps for one execution (one <see cref="Models.SyncJob"/> correlation id without a trailing state segment).
/// </summary>
/// <param name="SyncJobCorrelationId">Base correlation id for the run (no <c>/Created</c> suffix).</param>
public sealed record SyncJobRunAggregate(string SyncJobCorrelationId)
{
    /// <summary>Latest activity time across all steps in this run.</summary>
    public DateTimeOffset LastActivity { get; set; }

    /// <summary>Last occurrence per progress state name (e.g. Created, Started, Done).</summary>
    public Dictionary<string, SyncJobProgressStepAggregate> StepsByState { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
