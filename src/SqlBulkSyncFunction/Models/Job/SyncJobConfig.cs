namespace SqlBulkSyncFunction.Models.Job
{
    public record SyncJobConfig
    {
        public SyncJobConfigDataSource Source { get; set; }
        public SyncJobConfigDataSource Target { get; set; }
        public string[] Tables { get; set; }
        public int? BatchSize { get; set; }
    }
}