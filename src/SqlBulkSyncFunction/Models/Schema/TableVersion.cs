using System;

namespace SqlBulkSyncFunction.Models.Schema
{
    public record TableVersion
    {
        public string TableName { get; set; }
        public long CurrentVersion { get; set; }
        public long MinValidVersion { get; set; }
        public DateTimeOffset Queried { get; set; }
    };
}
