using System.Collections.Concurrent;
using System.Threading.Tasks;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Services
{
    public interface ITokenCacheService
    {
        Task<ConcurrentDictionary<string, string>> GetTokenCache(SyncJobsConfig syncJobsConfig);
    }
}