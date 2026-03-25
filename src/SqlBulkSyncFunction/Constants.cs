namespace SqlBulkSyncFunction;

/// <summary>
/// Constants used throughout the application.
/// </summary>
public static class Constants
{
    public static class Queues
    {
        /// <summary>
        /// Queue name for processing global change tracking jobs.
        /// </summary>
        public const string ProcessGlobalChangeTrackingQueue = "processglobalchangetrackingqueue";

        /// <summary>
        /// Queue name for logging global change tracking schedule.
        /// </summary>
        public const string LogScheduleQueue = "logschedule";

        /// <summary>
        /// Sync progress queue name.
        /// </summary>
        public const string SyncJobProgressQueue = "syncjobprogress";
    }
    /// <summary>
    /// NCRONTAB expressions and configuration keys for <see cref="Functions.ProcessGlobalChangeTrackingSchedule"/> timer triggers.
    /// </summary>
    public static class Schedules
    {
        /// <summary>
        /// Configuration / environment key for the <c>Custom</c> schedule (same as the timer binding placeholder without % delimiters).
        /// </summary>
        public const string CustomScheduleConfigurationKey = "ProcessGlobalChangeTrackingSchedule";

        /// <summary>
        /// Timer trigger binding for the custom schedule: <c>%ProcessGlobalChangeTrackingSchedule%</c>.
        /// </summary>
        public const string CustomScheduleTimerTrigger = "%" + CustomScheduleConfigurationKey + "%";

        /// <summary>NCRONTAB for <c>Midnight</c> (<c>0 0 0 * * *</c>).</summary>
        public const string MidnightCron = "0 0 0 * * *";

        /// <summary>NCRONTAB for <c>Noon</c> (<c>0 0 12 * * *</c>).</summary>
        public const string NoonCron = "0 0 12 * * *";

        /// <summary>NCRONTAB for <c>EveryFiveMinutes</c> (<c>5 */5 * * * *</c>).</summary>
        public const string EveryFiveMinutesCron = "5 */5 * * * *";

        /// <summary>NCRONTAB for <c>EveryHour</c> (<c>10 0 * * * *</c>).</summary>
        public const string EveryHourCron = "10 0 * * * *";
    }

    public static class Containers
    {
        /// <summary>
        /// Blob container name for sync job states.
        /// </summary>
        public const string SyncJob = "syncjob";

        public const string SyncSchedule = "syncschedule";

        /// <summary>
        /// Blob container for per-job monitoring aggregates (written by the aggregation timer).
        /// </summary>
        public const string Monitor = "monitor";
    }

    /// <summary>
    /// Content types for Azure Blob uploads.
    /// </summary>
    public static class BlobContentTypes
    {
        /// <summary>JSON documents (UTF-8).</summary>
        public const string Json = "application/json; charset=utf-8";
    }
}
