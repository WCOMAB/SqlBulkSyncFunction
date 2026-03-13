using System;

namespace SqlBulkSyncFunction.Models;

public record SyncJobProgress(
    string Area,
    string ConfigurationId,
    string Schedule,
    string ScheduleCorrelationId,
    string SyncJobCorrelationId,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    SyncJobProgressState State,
    string Message = null
    )
{
    public string CorrelationId { get; init; } = GetCorrelationId(SyncJobCorrelationId, State);

    public DateTimeOffset Occured { get; init; } = DateTimeOffset.UtcNow;

    public SyncJobProgress WithState(SyncJobProgressState state, string message = null)
        => this with
        {
            State = state,
            Message = message,
            Occured = DateTimeOffset.UtcNow,
            CorrelationId = GetCorrelationId(SyncJobCorrelationId, state)
        };

    private static string GetCorrelationId(string syncJobCorrelationId, SyncJobProgressState state)
        => FormattableString.Invariant($"{syncJobCorrelationId}/{state}");
}
