using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlBulkSyncFunction.Models.Job
{
    public record SyncJobsConfig
    {
        private static readonly string[] DefaultJobSchedules = {"Custom"};

        public Dictionary<string, SyncJobConfig> Jobs { get; init; } 

        public Lazy<ILookup<string, (string Key, SyncJobConfig Job)>> ScheduledJobs { get; }
        private static Dictionary<string, SyncJobConfig> Empty { get; } = new Dictionary<string, SyncJobConfig>(0);

        private ILookup<string, (string Key, SyncJobConfig Job)> GetScheduledJobs()
            => (
                from job in Jobs ?? Empty
                where !job.Value.Manual.HasValue || job.Value.Manual == false
                from schedule in GetJobSchedules(job)
                select (Key: schedule, job.Value)
            ).ToLookup(
                key => key.Key,
                value => value,
                StringComparer.OrdinalIgnoreCase
            );

        private static IEnumerable<string> GetJobSchedules(KeyValuePair<string, SyncJobConfig> job)
            => (
                job.Value.Schedules == null
                ||
                job.Value.Schedules.Count == 0
            )
                ? DefaultJobSchedules
                : job.Value.Schedules
                    .Where(value=>value.Value)
                    .Select(key=>key.Key);

        public SyncJobsConfig()
        {
            ScheduledJobs = new Lazy<ILookup<string, (string Key, SyncJobConfig Job)>>(GetScheduledJobs);
        }
    }
}