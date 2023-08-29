using System;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace SqlBulkSyncFunction.Services;

public record AzureSqlTokenService(DefaultAzureCredential DefaultAzureCredential) : IAzureSqlTokenService
{
    private const string AzureSqlResourceId = "https://database.windows.net/";
    private static AccessToken? AccessToken = default;


    public async Task<string> GetAccessToken(string tenantId)
        => (
                AccessToken is { } validToken && validToken.ExpiresOn < DateTimeOffset.UtcNow.AddMinutes(5)
                        ? validToken
                        : (AccessToken = await DefaultAzureCredential.GetTokenAsync(
                            new TokenRequestContext(
                                new[] { AzureSqlResourceId },
                                tenantId: string.IsNullOrWhiteSpace(tenantId) ? null : tenantId
                                )
                            )
                ).Value
            ).Token;
}

