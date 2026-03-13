using System;
using Microsoft.Extensions.Logging;

namespace SqlBulkSyncFunction.Services;

public partial class ProcessSyncJobService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Connecting to source database {DataSource}.{Database}")]
    private partial void LogConnectingToSourceDatabase(string schedule, string id, string area, string dataSource, string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Connected {ClientConnectionId}")]
    private partial void LogConnected(string schedule, string id, string area, Guid clientConnectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Connecting to target database {DataSource}.{Database}")]
    private partial void LogConnectingToTargetDatabase(string schedule, string id, string area, string dataSource, string database);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Ensuring sync schema and table exists...")]
    private partial void LogEnsuringSyncSchemaAndTableExists(string schedule, string id, string area);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Ensured sync schema and table exist")]
    private partial void LogEnsuredSyncSchemaAndTableExist(string schedule, string id, string area);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Fetching table schemas...")]
    private partial void LogFetchingTableSchemas(string schedule, string id, string area);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Found {TableCount} tables, duration {Elapsed}")]
    private partial void LogFoundTablesDuration(string schedule, string id, string area, int tableCount, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Begin {TableSchemaScope}")]
    private partial void LogBeginTableSchemaScope(string schedule, string id, string area, string tableSchemaScope);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} Already up to date")]
    private partial void LogAlreadyUpToDate(string schedule, string id, string area);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Schedule} {Id} {Area} Unknown / failed to fetch source version {Scope}.")]
    private partial void LogUnknownSourceVersion(string schedule, string id, string area, string scope);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Schedule} {Id} {Area} End {TableSchemaScope}, duration {Elapsed}")]
    private partial void LogEndTableSchemaScopeDuration(string schedule, string id, string area, string tableSchemaScope, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Schedule} {Id} {Area} Exception {TableSchemaScope}, duration {Elapsed}, exception: {Message}")]
    private partial void LogSyncException(Exception ex, string schedule, string id, string area, string tableSchemaScope, TimeSpan elapsed, string message);
}
