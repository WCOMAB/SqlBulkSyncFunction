using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

await new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(
        configure =>
        {

            _ = configure.AddOptions<SyncJobsConfig>()
                .Configure<IConfiguration>(
                    static (settings, configuration) => configuration.GetSection(nameof(SyncJobsConfig)).Bind(settings));

            _ = configure
                .AddSingleton<Azure.Identity.DefaultAzureCredential>()
                .AddSingleton<IAzureSqlTokenService, AzureSqlTokenService>()
                .AddSingleton<IProcessSyncJobService, ProcessSyncJobService>()
                .AddSingleton<ITokenCacheService, TokenCacheService>();

            _ = configure
                .AddApplicationInsightsTelemetryWorkerService()
                .ConfigureFunctionsApplicationInsights();
        })
    .Build()
    .RunAsync();
