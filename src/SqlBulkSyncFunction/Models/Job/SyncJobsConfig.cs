using System.Collections.Generic;

namespace SqlBulkSyncFunction.Models.Job
{
    public record SyncJobsConfig
    {
        public Dictionary<string, SyncJobConfig> Jobs { get; set; }
    }
}