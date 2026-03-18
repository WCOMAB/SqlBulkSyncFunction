using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlBulkSyncFunction.Functions;
using SqlBulkSyncFunction.Models.Job;
using SqlBulkSyncFunction.Services;

await new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureLogging(
        static (context, logging) => logging
                                        .AddConfiguration(context.Configuration.GetSection("Logging"))
                                        .SetMinimumLevel(LogLevel.Information)
                                        .AddSimpleConsole(static o => {})
                                        .AddFilter("SqlBulkSyncFunction", LogLevel.Information))
    .ConfigureServices(
        static configure =>
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
                    static az => {
                        var connectionString = System.Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                        _ = az
                            .AddBlobServiceClient(connectionString).ConfigureOptions(
                                static options => {
                                    options.Diagnostics.IsLoggingContentEnabled = false;
                                    options.Diagnostics.IsLoggingEnabled = false;
                                }
                                );
                        _ = az
                            .AddQueueServiceClient(connectionString)
                            .ConfigureOptions(
                                static options => {
                                    options.MessageEncoding = Azure.Storage.Queues.QueueMessageEncoding.Base64;
                                    options.Diagnostics.IsLoggingContentEnabled = false;
                                    options.Diagnostics.IsLoggingEnabled = false;
                                }
                            );
                    }
                );

            _ = configure
                .AddOpenTelemetry()
                .UseFunctionsWorkerDefaults()
                .UseAzureMonitorExporter();
        })
    .Build()
    .RunAsync();
