using System;
using System.Collections.Concurrent;
using Cronos;
using Microsoft.Extensions.Configuration;
using SqlBulkSyncFunction.Functions;

namespace SqlBulkSyncFunction.Services;

/// <summary>
/// Estimates the next Azure Functions timer fire time for <see cref="ProcessGlobalChangeTrackingSchedule"/>
/// using the same NCRONTAB expressions as the <c>TimerTrigger</c> attributes (including <c>Custom</c> from app settings).
/// Parsed cron expressions are cached per schedule name so repeated lookups avoid resolving cron and configuration.
/// </summary>
/// <param name="configuration">Application configuration (used for the <c>Custom</c> schedule expression).</param>
public sealed class ProcessGlobalChangeTrackingScheduleNextRun(IConfiguration configuration)
{
    /// <summary>
    /// Parsed <see cref="CronExpression"/> instances keyed by normalized schedule name (trimmed; <see cref="StringComparer.Ordinal"/>).
    /// If the <c>Custom</c> cron in configuration changes at runtime, restart the worker to pick up a new expression.
    /// </summary>
    private readonly ConcurrentDictionary<string, CronExpression> _expressionsByScheduleName = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the next scheduled run strictly after <paramref name="fromUtc"/> when the expression is valid.
    /// </summary>
    /// <param name="scheduleName">Schedule key (e.g. <c>EveryFiveMinutes</c>, <c>Custom</c>).</param>
    /// <param name="fromUtc">Reference instant (typically UTC now).</param>
    /// <param name="nextRunUtc">Next occurrence in UTC, if resolved.</param>
    /// <returns><see langword="true"/> when a cron expression was resolved and the next occurrence exists.</returns>
    public bool TryGetNextRunUtc(string scheduleName, DateTimeOffset fromUtc, out DateTimeOffset nextRunUtc)
    {
        nextRunUtc = default;
        if (string.IsNullOrWhiteSpace(scheduleName))
        {
            return false;
        }

        if (!TryParseScheduleCron(scheduleName, out var expression))
        {
            return false;
        }

        var from = fromUtc.UtcDateTime;
        var next = expression.GetNextOccurrence(from, TimeZoneInfo.Utc, inclusive: false);
        if (!next.HasValue)
        {
            return false;
        }

        nextRunUtc = new DateTimeOffset(DateTime.SpecifyKind(next.Value, DateTimeKind.Utc), TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// Maps a schedule name to the same cron string as <see cref="ProcessGlobalChangeTrackingSchedule"/>, parses it, and caches the result by schedule name.
    /// </summary>
    /// <param name="scheduleName">Schedule key from configuration (e.g. <c>Midnight</c>).</param>
    /// <param name="expression">Parsed expression, or <see langword="null"/> when unresolved or invalid.</param>
    /// <returns><see langword="true"/> when a valid expression was resolved.</returns>
    public bool TryParseScheduleCron(string scheduleName, out CronExpression expression)
    {
        expression = null;
        if (string.IsNullOrWhiteSpace(scheduleName))
        {
            return false;
        }

        var cacheKey = scheduleName.Trim();
        if (_expressionsByScheduleName.TryGetValue(cacheKey, out var cached))
        {
            expression = cached;
            return true;
        }

        if (!TryResolveCronString(scheduleName, out var cron) || string.IsNullOrWhiteSpace(cron))
        {
            return false;
        }

        try
        {
            var parsed = CronExpression.Parse(cron, CronFormat.IncludeSeconds);
            expression = _expressionsByScheduleName.GetOrAdd(cacheKey, parsed);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the NCRONTAB string for <paramref name="scheduleName"/> (including <c>Custom</c> from configuration).
    /// </summary>
    /// <param name="scheduleName">Schedule key (method name on <see cref="ProcessGlobalChangeTrackingSchedule"/>).</param>
    /// <param name="cron">Cron string when recognized; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a cron string was associated with the schedule name.</returns>
    private bool TryResolveCronString(string scheduleName, out string cron)
    {
        cron = null;
        var key = scheduleName.Trim();
        cron = key switch
        {
            _ when string.Equals(key, nameof(ProcessGlobalChangeTrackingSchedule.Midnight), StringComparison.Ordinal) => Constants.Schedules.MidnightCron,
            _ when string.Equals(key, nameof(ProcessGlobalChangeTrackingSchedule.Noon), StringComparison.Ordinal) => Constants.Schedules.NoonCron,
            _ when string.Equals(key, nameof(ProcessGlobalChangeTrackingSchedule.EveryFiveMinutes), StringComparison.Ordinal) => Constants.Schedules.EveryFiveMinutesCron,
            _ when string.Equals(key, nameof(ProcessGlobalChangeTrackingSchedule.EveryHour), StringComparison.Ordinal) => Constants.Schedules.EveryHourCron,
            _ when string.Equals(key, nameof(ProcessGlobalChangeTrackingSchedule.Custom), StringComparison.Ordinal)
                => configuration[Constants.Schedules.CustomScheduleConfigurationKey]?.Trim(),
            _ => null,
        };

        return !string.IsNullOrWhiteSpace(cron);
    }
}
