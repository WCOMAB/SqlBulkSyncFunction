using System.Threading.Tasks;

namespace SqlBulkSyncFunction.Services
{
    public interface IAzureSqlTokenService
    {
        Task<string> GetAccessToken(string tenantId);
    }
}