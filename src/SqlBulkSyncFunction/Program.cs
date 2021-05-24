using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

await new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(
        configure => {
            configure.AddOptions<SyncJobsConfig>()
                .Configure<IConfiguration>(
                    (settings, configuration) => configuration.GetSection(nameof(SyncJobsConfig)).Bind(settings));

            configure
                .AddSingleton<Microsoft.Azure.Services.AppAuthentication.AzureServiceTokenProvider>()
                .AddSingleton<IAzureSqlTokenService, AzureSqlTokenService>()
                .AddSingleton<IProcessSyncJobService, ProcessSyncJobService>()
                .AddSingleton<ITokenCacheService, TokenCacheService>();
        })
    .Build()
    .RunAsync();