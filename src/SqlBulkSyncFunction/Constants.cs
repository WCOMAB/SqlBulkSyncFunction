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
    public static class Containers
    {
        /// <summary>
        /// Blob container name for sync job states.
        /// </summary>
        public const string SyncJob = "syncjob";

        public const string SyncSchedule = "syncschedule";
    }
}
