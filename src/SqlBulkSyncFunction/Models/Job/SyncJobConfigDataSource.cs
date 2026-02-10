namespace SqlBulkSyncFunction.Models.Job;

public record SyncJobConfigDataSource
{
    public string ConnectionString { get; set; }
    public bool ManagedIdentity { get; set; }
    public string TenantId { get; set; }
}
