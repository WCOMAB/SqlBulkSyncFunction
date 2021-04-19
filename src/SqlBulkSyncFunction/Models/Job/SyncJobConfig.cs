using System.Collections.Generic;

namespace SqlBulkSyncFunction.Models.Job
{
    public record SyncJobConfig
    {
        public SyncJobConfigDataSource Source { get; set; }
        public SyncJobConfigDataSource Target { get; set; }
        public Dictionary<string, string> Tables { get; set; }
        public Dictionary<string, string> TargetTables { get; set; }
        public int? BatchSize { get; set; }
        public string Area { get; set; }
        public bool? Manual { get; set; }
    }
}