using System.Threading.Tasks;

namespace SqlBulkSyncFunction.Services
{
    public interface IAzureSqlTokenService
    {
        public Task<string> GetAccessToken(string tenantId);
    }
}
