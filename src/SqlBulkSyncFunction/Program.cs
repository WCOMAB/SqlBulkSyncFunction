using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

await new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(
        configure =>
        {
            configure.AddOptions<SyncJobsConfig>()
                .Configure<IConfiguration>(
                    (settings, configuration) => configuration.GetSection(nameof(SyncJobsConfig)).Bind(settings));

            configure
                .AddSingleton<Azure.Identity.DefaultAzureCredential>()
                .AddSingleton<IAzureSqlTokenService, AzureSqlTokenService>()
                .AddSingleton<IProcessSyncJobService, ProcessSyncJobService>()
                .AddSingleton<ITokenCacheService, TokenCacheService>();
        })
    .Build()
    .RunAsync();
