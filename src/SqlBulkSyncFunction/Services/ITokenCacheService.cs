using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Services
{
    public interface ITokenCacheService
    {
        Task<ConcurrentDictionary<string, string>> GetTokenCache(IEnumerable<SyncJobConfig> jobs);
        Task<ConcurrentDictionary<string, string>> GetTokenCache(SyncJobConfig job);
    }
}