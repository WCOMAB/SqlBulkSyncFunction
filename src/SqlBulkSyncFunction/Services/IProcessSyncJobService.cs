using System.Threading.Tasks;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Services;

public interface IProcessSyncJobService
{
    public Task ProcessSyncJob(SyncJob syncJob, bool globalChangeTracking);
}
