using System;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace SqlBulkSyncFunction.Services;

public partial class AzureSqlTokenService(DefaultAzureCredential defaultAzureCredential) : IAzureSqlTokenService
{
    private const string AzureSqlResourceId = "https://database.windows.net/";
    private static AccessToken? _accessToken = default;


    public async Task<string> GetAccessToken(string tenantId)
        => (
                _accessToken is { } validToken && validToken.ExpiresOn < DateTimeOffset.UtcNow.AddMinutes(5)
                        ? validToken
                        : (_accessToken = await defaultAzureCredential.GetTokenAsync(
                            new TokenRequestContext(
                                [AzureSqlResourceId],
                                tenantId: string.IsNullOrWhiteSpace(tenantId) ? null : tenantId
                                )
                            )
                ).Value
            ).Token;
}

