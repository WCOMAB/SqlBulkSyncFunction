using System.Collections.Generic;

namespace SqlBulkSyncFunction.Models.Schema;

/// <summary>
/// Represents primary key-only change tracking details for a single table, grouped by change operation.
/// </summary>
/// <param name="Updated">Changed rows where <c>SYS_CHANGE_OPERATION = N'U'</c>.</param>
/// <param name="Inserted">Changed rows where <c>SYS_CHANGE_OPERATION = N'I'</c>.</param>
/// <param name="Deleted">Changed rows where <c>SYS_CHANGE_OPERATION = N'D'</c>.</param>
public record SchemaTrackingPrimaryKeyDetailsResponse(
    IReadOnlyList<IReadOnlyDictionary<string, object>> Updated,
    IReadOnlyList<IReadOnlyDictionary<string, object>> Inserted,
    IReadOnlyList<IReadOnlyDictionary<string, object>> Deleted
);
