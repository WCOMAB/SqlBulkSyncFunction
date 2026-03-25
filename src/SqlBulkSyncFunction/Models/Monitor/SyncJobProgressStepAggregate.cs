using System;

namespace SqlBulkSyncFunction.Models.Monitor;

/// <summary>
/// One progress state occurrence within a run.
/// </summary>
/// <param name="Occured">When this state was reported.</param>
/// <param name="Message">Optional message (e.g. exception text).</param>
public sealed record SyncJobProgressStepAggregate(
    DateTimeOffset Occured,
    string Message
);
