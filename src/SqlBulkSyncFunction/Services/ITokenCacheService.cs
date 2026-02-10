using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SqlBulkSyncFunction.Models.Job;

namespace SqlBulkSyncFunction.Services;

public interface ITokenCacheService
{
    public Task<ConcurrentDictionary<string, string>> GetTokenCache(IEnumerable<SyncJobConfig> jobs);
    public Task<ConcurrentDictionary<string, string>> GetTokenCache(SyncJobConfig job);
}
