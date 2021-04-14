using System.Threading.Tasks;
using Microsoft.Azure.Services.AppAuthentication;

namespace SqlBulkSyncFunction.Services
{
    // ReSharper disable once UnusedMember.Global
    public record AzureSqlTokenService(AzureServiceTokenProvider AzureServiceTokenProvider) : IAzureSqlTokenService
    {
        private const string AzureSqlResourceId = "https://database.windows.net/";

        public async Task<string> GetAccessToken(string tenantId)
            => await AzureServiceTokenProvider.GetAccessTokenAsync(AzureSqlResourceId, string.IsNullOrWhiteSpace(tenantId) ? null : tenantId);
    }
}
