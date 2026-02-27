using System;
using Microsoft.Extensions.Logging;

namespace SqlBulkSyncFunction.Functions;

public partial class ProcessGlobalChangeTrackingSchedule
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Config} IsPastDue ({IsPastDue}) skipping.")]
    private partial void LogIsPastDueSkipping(string config, bool isPastDue);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Config} no jobs configured, skipping.")]
    private partial void LogNoJobsConfiguredSkipping(string config);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Config} Found {Length} jobs for schedule.")]
    private partial void LogFoundJobsForSchedule(string config, int length);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process {Config}")]
    private partial void LogFailedToProcess(Exception ex, string config);
}
