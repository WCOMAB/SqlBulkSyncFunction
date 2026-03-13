using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlBulkSyncFunction.Functions;
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

            configure
                .AddSingleton<Azure.Identity.DefaultAzureCredential>()
                .AddSingleton<IAzureSqlTokenService, AzureSqlTokenService>()
                .AddSingleton<IProcessSyncJobService, ProcessSyncJobService>()
                .AddSingleton<ITokenCacheService, TokenCacheService>()
                .AddSingleton<SyncProgressService>()
                .AddAzureClients(
                    az => {
                        var connectionString = System.Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                        _ = az.AddBlobServiceClient(connectionString);
                        _ = az
                            .AddQueueServiceClient(connectionString)
                            .ConfigureOptions(options => options.MessageEncoding = Azure.Storage.Queues.QueueMessageEncoding.Base64);
                    }
                );

            _ = configure
                .AddApplicationInsightsTelemetryWorkerService()
                .ConfigureFunctionsApplicationInsights();
        })
    .Build()
    .RunAsync();
