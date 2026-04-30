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

        /// <summary>
        /// Main queue for schema tracking data export jobs (message body: full correlation id path).
        /// </summary>
        public const string ExportJob = "exportjob";

        /// <summary>
        /// Queue for export workers that build the updated-rows ZIP from change tracking.
        /// </summary>
        public const string ExportJobUpdated = "exportjob-updated";

        /// <summary>
        /// Queue for export workers that build the inserted-rows ZIP from change tracking.
        /// </summary>
        public const string ExportJobInserted = "exportjob-inserted";

        /// <summary>
        /// Queue for export workers that build the deleted-rows ZIP from change tracking.
        /// </summary>
        public const string ExportJobDeleted = "exportjob-deleted";

        /// <summary>
        /// Signaled when the updated ZIP has been written for an export job.
        /// </summary>
        public const string ExportJobUpdatedDone = ExportJobUpdated + "-done";

        /// <summary>
        /// Signaled when the inserted ZIP has been written for an export job.
        /// </summary>
        public const string ExportJobInsertedDone = ExportJobInserted + "-done";

        /// <summary>
        /// Signaled when the deleted ZIP has been written for an export job.
        /// </summary>
        public const string ExportJobDeletedDone = ExportJobDeleted + "-done";

        /// <summary>
        /// Updated segment only: message body is the correlation id when <c>ProcessExportSegmentAsync</c> throws during processing (SQL/ZIP/blob). Invalid queue bodies are rejected at the trigger with <c>ArgumentException.ThrowIfNullOrEmpty</c>.
        /// </summary>
        public const string ExportJobUpdatedError = ExportJobUpdated + "-error";

        /// <summary>
        /// Inserted segment only: correlation id when segment processing throws.
        /// </summary>
        public const string ExportJobInsertedError = ExportJobInserted + "-error";

        /// <summary>
        /// Deleted segment only: correlation id when segment processing throws.
        /// </summary>
        public const string ExportJobDeletedError = ExportJobDeleted + "-error";
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

        /// <summary>
        /// Blob container for schema tracking export requests, jobs, response ZIPs, and result metadata.
        /// </summary>
        public const string Export = "export";
    }

    /// <summary>
    /// Azure Table Storage table names used by the function app.
    /// </summary>
    public static class Tables
    {
        /// <summary>
        /// Table storing export job state (partition: area_id_tableId, row: export job guid).
        /// </summary>
        public const string ExportJobs = "exportjobs";
    }

    /// <summary>
    /// Content types for Azure Blob uploads.
    /// </summary>
    public static class BlobContentTypes
    {
        /// <summary>JSON documents (UTF-8).</summary>
        public const string Json = "application/json; charset=utf-8";

        /// <summary>ZIP archives.</summary>
        public const string Zip = "application/zip";
    }
}
