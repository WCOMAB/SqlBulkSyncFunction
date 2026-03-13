using System.Threading;
using System.Threading.Tasks;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Services;

public interface IProcessSyncJobService
{
    public Task EnqueueSyncJob(SyncJob syncJob, CancellationToken cancellationToken);
    public Task ProcessSyncJob(SyncJob syncJob, bool globalChangeTracking, CancellationToken cancellationToken);
}
