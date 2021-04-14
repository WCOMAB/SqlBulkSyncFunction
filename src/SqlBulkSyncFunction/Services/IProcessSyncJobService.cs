using System.Threading.Tasks;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Services
{
    public interface IProcessSyncJobService
    {
        Task ProcessSyncJob(SyncJob syncJob, bool globalChangeTracking);
    }
}