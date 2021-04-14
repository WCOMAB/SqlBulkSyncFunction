namespace SqlBulkSyncFunction.Models.Job
{
    public record SyncJobsConfig
    {
        public SyncJobConfig[] Jobs { get; set; }
    }
}