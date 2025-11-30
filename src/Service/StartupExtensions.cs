// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO.Abstractions;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Service.HealthCheck;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Telemetry;
using Azure.DataApiBuilder.Service.Utilities;
using Azure.Identity;
using Azure.Monitor.Ingestion;
using HotChocolate.Execution;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
using CorsOptions = Azure.DataApiBuilder.Config.ObjectModel.CorsOptions;
using System.Text.Json;

namespace Azure.DataApiBuilder.Service;

/// <summary>
/// Extension methods for configuring services and pipeline from the Startup class
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Configures all services for the application
    /// </summary>
    public static IServiceCollection ConfigureServices(this WebApplicationBuilder builder)
    {
        StartupConfiguration.AddValidFilters();

        IServiceCollection services = builder.Services;
        IConfiguration configuration = builder.Configuration;

        services.AddSingleton(new HotReloadEventHandler<HotReloadEventArgs>());

        string configFileName = configuration.GetValue<string>("ConfigFileName") ?? FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME;
        string? connectionString = configuration.GetValue<string?>(
            FileSystemRuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING.Replace(FileSystemRuntimeConfigLoader.ENVIRONMENT_PREFIX, ""),
            null);

        string configSource = configuration.GetValue<string>("ConfigSource")
            ?? Environment.GetEnvironmentVariable("DAB_CONFIG_SOURCE")
            ?? "FileSystem";

        IFileSystem fileSystem = new FileSystem();
        services.AddSingleton<IFileSystem>(fileSystem);

        Console.WriteLine($"[StartupExtensions] Configuring services. Source: {configSource}.");

        RuntimeConfigLoader configLoader = CreateConfigLoader(services, configuration, configSource, configFileName, connectionString, fileSystem);
        services.AddSingleton(configLoader);
        Console.WriteLine($"[StartupExtensions] Runtime config loader registered: {configLoader.GetType().Name}.");

        RuntimeConfigProvider configProvider = new(configLoader);
        services.AddSingleton(configProvider);

        Console.WriteLine("[StartupExtensions] Attempting initial runtime config load.");
        bool runtimeConfigAvailable = configProvider.TryGetConfig(out RuntimeConfig? runtimeConfig);
        Console.WriteLine($"[StartupExtensions] Initial runtime config load returned: {runtimeConfigAvailable}.");

        ConfigureTelemetry(services, runtimeConfigAvailable, runtimeConfig);
        ConfigureLoggers(services, configProvider);
        ConfigureDatabaseServices(services);
        ConfigureHealthChecks(services);
        ConfigureHttpClients(services);
        ConfigureAuthenticationAndAuthorization(services, configProvider, runtimeConfig);
        ConfigureGraphQL(services, runtimeConfig);
        ConfigureCaching(services, runtimeConfigAvailable, runtimeConfig);

        services.AddDabMcpServer(configProvider);
        services.AddControllers();

        return services;
    }

    /// <summary>
    /// Configures the HTTP request pipeline
    /// </summary>
    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        var runtimeConfigProvider = app.Services.GetRequiredService<RuntimeConfigProvider>();
        var hotReloadEventHandler = app.Services.GetRequiredService<HotReloadEventHandler<HotReloadEventArgs>>();
        var env = app.Environment;

        Console.WriteLine($"[StartupExtensions] Configure pipeline for environment {env.EnvironmentName}.");

        bool isRuntimeReady = false;

        if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
        {
            Console.WriteLine("[StartupExtensions] Runtime config pre-loaded. Beginning initialization.");
            isRuntimeReady = PerformOnConfigChangeAsync(app).Result;
            Console.WriteLine($"[StartupExtensions] Engine initialization result: {isRuntimeReady}.");

            if (!isRuntimeReady)
            {
                var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("StartupExtensions");
                logger.LogError("Could not initialize the engine with the runtime config file: {configFilePath}", runtimeConfigProvider.ConfigFilePath);
                app.Lifetime.StopApplication();
            }
        }
        else
        {
            Console.WriteLine("[StartupExtensions] Runtime config not yet available. Waiting for late binding.");
            runtimeConfigProvider.IsLateConfigured = true;
            runtimeConfigProvider.RuntimeConfigLoadedHandlers.Add(async (_, _) =>
            {
                Console.WriteLine("[StartupExtensions] Late-bound config received. Re-initializing engine.");
                isRuntimeReady = await PerformOnConfigChangeAsync(app);
                Console.WriteLine($"[StartupExtensions] Late-bound engine initialization result: {isRuntimeReady}.");
                return isRuntimeReady;
            });
        }

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        if (!Program.IsHttpsRedirectionDisabled)
        {
            app.UseWhen(
                context => !(context.Request.Path.StartsWithSegments("/health") || context.Request.Path.StartsWithSegments("/graphql")),
                appBuilder => appBuilder.UseHttpsRedirection()
            );
        }

        app.UseCorrelationIdMiddleware();
        app.UsePathRewriteMiddleware();

        if (StartupConfiguration.IsUIEnabled(runtimeConfig, env))
        {
            app.UseSwaggerUI(c =>
            {
                c.ConfigObject.Urls = new SwaggerEndpointMapper(app.Services.GetService<RuntimeConfigProvider?>());
            });
        }

        app.UseRouting();

        if (runtimeConfig is not null && runtimeConfig.Runtime?.Host?.Cors is not null)
        {
            app.UseCors(corsPolicyBuilder =>
            {
                CorsOptions corsConfig = runtimeConfig.Runtime.Host.Cors;
                StartupConfiguration.ConfigureCors(corsPolicyBuilder, corsConfig);
            });
        }

        app.Use(async (context, next) =>
        {
            bool isHealthCheckRequest = context.Request.Path == "/" && context.Request.Method == "GET";
            bool isSettingConfig = context.Request.Path.StartsWithSegments("/configuration")
                && context.Request.Method == "POST";
            if (isRuntimeReady || isHealthCheckRequest)
            {
                await next.Invoke();
            }
            else if (isSettingConfig)
            {
                if (isRuntimeReady)
                {
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                }
                else
                {
                    await next.Invoke();
                }
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            }
        });

        app.UseAuthentication();
        app.UseClientRoleHeaderAuthenticationMiddleware();
        app.UseAuthorization();
        app.UseClientRoleHeaderAuthorizationMiddleware();

        IRequestExecutorManager requestExecutorManager = app.Services.GetRequiredService<IRequestExecutorManager>();
        hotReloadEventHandler.Subscribe(
            "GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED",
            (_, _) => StartupConfiguration.EvictGraphQLSchema(requestExecutorManager));

        app.MapControllers();
        app.MapDabMcp(runtimeConfigProvider);
        app.MapGraphQL().WithOptions(new HotChocolate.AspNetCore.GraphQLServerOptions
        {
            Tool = { Enable = StartupConfiguration.IsUIEnabled(runtimeConfig, env) }
        });
        app.MapNitroApp().WithOptions(new HotChocolate.AspNetCore.GraphQLToolOptions { Enable = false });
        app.MapHealthChecks("/", new HealthCheckOptions
        {
            ResponseWriter = app.Services.GetRequiredService<BasicHealthReportResponseWriter>().WriteResponse
        });

        return app;
    }

    private static RuntimeConfigLoader CreateConfigLoader(
        IServiceCollection services,
        IConfiguration configuration,
        string configSource,
        string configFileName,
        string? connectionString,
        IFileSystem fileSystem)
    {
        var hotReloadEventHandler = services.BuildServiceProvider().GetRequiredService<HotReloadEventHandler<HotReloadEventArgs>>();

        return configSource.ToLowerInvariant() switch
        {
            "cosmosdb" => CreateCosmosDbConfigLoader(services, configuration, hotReloadEventHandler, connectionString),
            _ => new FileSystemRuntimeConfigLoader(fileSystem, hotReloadEventHandler, configFileName, connectionString)
        };
    }

    private static RuntimeConfigLoader CreateCosmosDbConfigLoader(
        IServiceCollection services,
        IConfiguration configuration,
        HotReloadEventHandler<HotReloadEventArgs> hotReloadEventHandler,
        string? connectionString)
    {
        Console.WriteLine("[StartupExtensions] Config source resolved to Cosmos DB.");

        string cosmosConnectionString = configuration.GetConnectionString("ConfigurationCdb")
            ?? Environment.GetEnvironmentVariable("DAB_COSMOS_CONNECTION_STRING")
            ?? throw new InvalidOperationException("CosmosDB connection string missing.");

        // TEMPORARY: Log full connection string for debugging
        Console.WriteLine($"[StartupExtensions] FULL Cosmos connection string: {cosmosConnectionString}");

        string databaseName = configuration.GetValue<string>("CosmosDB:DatabaseName")
            ?? Environment.GetEnvironmentVariable("DAB_COSMOS_DATABASE") ?? "dab-config";
        string containerName = configuration.GetValue<string>("CosmosDB:ContainerName")
            ?? Environment.GetEnvironmentVariable("DAB_COSMOS_CONTAINER") ?? "configurations";
        string documentId = configuration.GetValue<string>("CosmosDB:DocumentId")
            ?? Environment.GetEnvironmentVariable("DAB_COSMOS_DOCUMENT_ID") ?? "runtime-config";
        string partitionKey = configuration.GetValue<string>("CosmosDB:PartitionKey")
            ?? Environment.GetEnvironmentVariable("DAB_COSMOS_PARTITION_KEY") ?? "production";

        Console.WriteLine($"[StartupExtensions] Database: {databaseName}, Container: {containerName}");
        Console.WriteLine($"[StartupExtensions] Document ID: {documentId}, Partition Key: {partitionKey}");

        string? connectionModeSetting = configuration.GetValue<string>("CosmosDB:ConnectionMode")
            ?? Environment.GetEnvironmentVariable("DAB_COSMOS_CONNECTION_MODE");

        // Default to Gateway mode for better emulator compatibility
        ConnectionMode connectionMode = ConnectionMode.Gateway;
        if (!string.IsNullOrWhiteSpace(connectionModeSetting)
            && Enum.TryParse(connectionModeSetting, ignoreCase: true, out ConnectionMode parsedMode))
        {
            connectionMode = parsedMode;
        }

        Console.WriteLine($"[StartupExtensions] Using Cosmos DB ConnectionMode: {connectionMode}");

        CosmosClientOptions cosmosClientOptions = new()
        {
            AllowBulkExecution = false,
            ConnectionMode = connectionMode,
            EnableContentResponseOnWrite = false,
            RequestTimeout = TimeSpan.FromSeconds(60),
            MaxRetryAttemptsOnRateLimitedRequests = 3,
            MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }
        };

        if (connectionMode == ConnectionMode.Direct)
        {
            cosmosClientOptions.LimitToEndpoint = true;
            cosmosClientOptions.EnableTcpConnectionEndpointRediscovery = true;
            cosmosClientOptions.OpenTcpConnectionTimeout = TimeSpan.FromSeconds(10);
        }
        else
        {
            // For Gateway mode with emulator, limit to endpoint to avoid metadata service calls
            cosmosClientOptions.LimitToEndpoint = true;
        }

        string? bypassSetting = configuration.GetValue<string>("CosmosDB:BypassCertificateValidation")
            ?? Environment.GetEnvironmentVariable("DAB_COSMOS_BYPASS_CERT_VALIDATION");
        bool bypassCosmosCertificateValidation = string.Equals(bypassSetting, "true", StringComparison.OrdinalIgnoreCase);

        if (bypassCosmosCertificateValidation)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            };

            var httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            cosmosClientOptions.HttpClientFactory = () => httpClient;
            Console.WriteLine("[StartupExtensions] WARNING: Bypassing Cosmos DB TLS certificate validation (development use only).");
        }
        CosmosClient cosmosClient = new(cosmosConnectionString, cosmosClientOptions);
        services.AddSingleton(cosmosClient);

        return new CosmosDbRuntimeConfigLoader(
            cosmosClient,
            databaseName,
            containerName,
            documentId,
            partitionKey,
            hotReloadEventHandler,
            connectionString);
    }

    private static void ConfigureTelemetry(IServiceCollection services, bool runtimeConfigAvailable, RuntimeConfig? runtimeConfig)
    {
        if (runtimeConfigAvailable && runtimeConfig?.Runtime?.Telemetry?.ApplicationInsights is not null
            && runtimeConfig.Runtime.Telemetry.ApplicationInsights.Enabled)
        {
            services.AddApplicationInsightsTelemetry();
            services.AddSingleton<ITelemetryInitializer, AppInsightsTelemetryInitializer>();
        }

        if (runtimeConfigAvailable && runtimeConfig?.Runtime?.Telemetry?.OpenTelemetry is not null
            && runtimeConfig.Runtime.Telemetry.OpenTelemetry.Enabled)
        {
            services.Configure<OpenTelemetryLoggerOptions>(options =>
            {
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                options.IncludeFormattedMessage = true;
            });
            services.AddOpenTelemetry()
                .WithLogging(logging =>
                {
                    logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(runtimeConfig.Runtime.Telemetry.OpenTelemetry.ServiceName!))
                        .AddOtlpExporter(configure =>
                        {
                            configure.Endpoint = new Uri(runtimeConfig.Runtime.Telemetry.OpenTelemetry.Endpoint!);
                            configure.Headers = runtimeConfig.Runtime.Telemetry.OpenTelemetry.Headers;
                            configure.Protocol = OtlpExportProtocol.Grpc;
                        });
                })
                .WithMetrics(metrics =>
                {
                    metrics.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(runtimeConfig.Runtime.Telemetry.OpenTelemetry.ServiceName!))
                        .AddOtlpExporter(configure =>
                        {
                            configure.Endpoint = new Uri(runtimeConfig.Runtime.Telemetry.OpenTelemetry.Endpoint!);
                            configure.Headers = runtimeConfig.Runtime.Telemetry.OpenTelemetry.Headers;
                            configure.Protocol = OtlpExportProtocol.Grpc;
                        })
                        .AddMeter(TelemetryMetricsHelper.MeterName);
                })
                .WithTracing(tracing =>
                {
                    tracing.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(runtimeConfig.Runtime.Telemetry.OpenTelemetry.ServiceName!))
                        .AddHttpClientInstrumentation()
                        .AddHotChocolateInstrumentation()
                        .AddOtlpExporter(configure =>
                        {
                            configure.Endpoint = new Uri(runtimeConfig.Runtime.Telemetry.OpenTelemetry.Endpoint!);
                            configure.Headers = runtimeConfig.Runtime.Telemetry.OpenTelemetry.Headers;
                            configure.Protocol = OtlpExportProtocol.Grpc;
                        })
                        .AddSource(TelemetryTracesHelper.DABActivitySource.Name);
                });
        }

        if (runtimeConfigAvailable && runtimeConfig?.Runtime?.Telemetry?.AzureLogAnalytics is not null
            && StartupConfiguration.IsAzureLogAnalyticsAvailable(runtimeConfig.Runtime.Telemetry.AzureLogAnalytics))
        {
            services.AddSingleton<ICustomLogCollector, AzureLogAnalyticsCustomLogCollector>();
            services.AddSingleton<ILoggerProvider, AzureLogAnalyticsLoggerProvider>();
            services.AddSingleton(sp =>
            {
                AzureLogAnalyticsOptions options = runtimeConfig.Runtime.Telemetry.AzureLogAnalytics;
                ManagedIdentityCredential credential = new();
                LogsIngestionClient logsIngestionClient = new(new Uri(options.Auth!.DceEndpoint!), credential);
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger<AzureLogAnalyticsFlusherService>();
                return new AzureLogAnalyticsFlusherService(options, StartupConfiguration.CustomLogCollector, logsIngestionClient, logger);
            });
            services.AddHostedService(sp => sp.GetRequiredService<AzureLogAnalyticsFlusherService>());
        }

        if (runtimeConfigAvailable && runtimeConfig?.Runtime?.Telemetry?.File is not null
            && runtimeConfig.Runtime.Telemetry.File.Enabled)
        {
            services.AddSingleton(sp =>
            {
                FileSinkOptions options = runtimeConfig.Runtime.Telemetry.File;
                return new LoggerConfiguration().WriteTo.File(
                    path: options.Path,
                    rollingInterval: (RollingInterval)Enum.Parse(typeof(RollingInterval), options.RollingInterval),
                    retainedFileCountLimit: options.RetainedFileCountLimit,
                    fileSizeLimitBytes: options.FileSizeLimitBytes,
                    rollOnFileSizeLimit: true);
            });
            services.AddSingleton(sp => sp.GetRequiredService<LoggerConfiguration>().MinimumLevel.Verbose().CreateLogger());
        }
    }

    private static void ConfigureLoggers(IServiceCollection services, RuntimeConfigProvider configProvider)
    {
        var hotReloadEventHandler = services.BuildServiceProvider().GetRequiredService<HotReloadEventHandler<HotReloadEventArgs>>();

        services.AddSingleton(implementationFactory: serviceProvider =>
        {
            LogLevelInitializer logLevelInit = new(StartupConfiguration.MinimumLogLevel, typeof(RuntimeConfigValidator).FullName, configProvider, hotReloadEventHandler);
            ILoggerFactory? loggerFactory = StartupConfiguration.CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider as IServiceProvider ?? serviceProvider.GetService<IServiceProvider>()!, logLevelInit);
            return loggerFactory.CreateLogger<RuntimeConfigValidator>();
        });

        // Similar pattern for other logger registrations
    }

    private static void ConfigureDatabaseServices(IServiceCollection services)
    {
        services.AddSingleton<RuntimeConfigValidator>();
        services.AddSingleton<CosmosClientProvider>();
        services.AddSingleton<IAbstractQueryManagerFactory, QueryManagerFactory>();
        services.AddSingleton<IQueryEngineFactory, QueryEngineFactory>();
        services.AddSingleton<IMutationEngineFactory, MutationEngineFactory>();
        services.AddSingleton<IMetadataProviderFactory, MetadataProviderFactory>();
        services.AddSingleton<GraphQLSchemaCreator>();
        services.AddSingleton<GQLFilterParser>();
        services.AddSingleton<RequestValidator>();
        services.AddSingleton<RestService>();
        services.AddSingleton<IAuthorizationHandler, RestAuthorizationHandler>();
        services.AddSingleton<IAuthorizationResolver, AuthorizationResolver>();
        services.AddSingleton<IOpenApiDocumentor, OpenApiDocumentor>();
    }

    private static void ConfigureHealthChecks(IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<BasicHealthCheck>(nameof(BasicHealthCheck));
        services.AddSingleton<HealthCheckHelper>();
        services.AddSingleton<HttpUtilities>();
        services.AddSingleton<BasicHealthReportResponseWriter>();
        services.AddSingleton<ComprehensiveHealthReportResponseWriter>();
    }

    private static void ConfigureHttpClients(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddHttpClient("ContextConfiguredHealthCheckClient")
            .ConfigureHttpClient((serviceProvider, client) =>
            {
                int port = PortResolutionHelper.ResolveInternalPort();
                string baseUri = $"http://localhost:{port}";
                client.BaseAddress = new Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = TimeSpan.FromSeconds(200);
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                bool allowSelfSigned = Environment.GetEnvironmentVariable("USE_SELF_SIGNED_CERT")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
                HttpClientHandler handler = new();
                if (allowSelfSigned)
                {
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }

                return handler;
            });
    }

    private static void ConfigureAuthenticationAndAuthorization(
        IServiceCollection services,
        RuntimeConfigProvider configProvider,
        RuntimeConfig? runtimeConfig)
    {
        if (runtimeConfig is not null && runtimeConfig.Runtime?.Host?.Mode is HostMode.Development)
        {
            StartupConfiguration.ConfigureAuthenticationV2(services, configProvider);
        }
        else
        {
            StartupConfiguration.ConfigureAuthentication(services, configProvider);
        }

        services.AddAuthorization();
    }

    private static void ConfigureGraphQL(IServiceCollection services, RuntimeConfig? runtimeConfig)
    {
        var hotReloadEventHandler = services.BuildServiceProvider().GetRequiredService<HotReloadEventHandler<HotReloadEventArgs>>();
        services.AddGraphQLServices(runtimeConfig?.Runtime?.GraphQL);

        hotReloadEventHandler.Subscribe(
            DabConfigEvents.GRAPHQL_SCHEMA_REFRESH_ON_CONFIG_CHANGED,
            (_, _) => services.AddGraphQLServices(runtimeConfig?.Runtime?.GraphQL));
    }

    private static void ConfigureCaching(IServiceCollection services, bool runtimeConfigAvailable, RuntimeConfig? runtimeConfig)
    {
        IFusionCacheBuilder fusionCacheBuilder = services.AddFusionCache()
            .WithOptions(options =>
            {
                options.FactoryErrorsLogLevel = LogLevel.Debug;
                options.EventHandlingErrorsLogLevel = LogLevel.Debug;
                string? cachePartition = runtimeConfig?.Runtime?.Cache?.Level2?.Partition;
                if (!string.IsNullOrWhiteSpace(cachePartition))
                {
                    options.CacheKeyPrefix = cachePartition + "_";
                    options.BackplaneChannelPrefix = cachePartition + "_";
                }
            })
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromSeconds(RuntimeCacheOptions.DEFAULT_TTL_SECONDS),
                ReThrowBackplaneExceptions = false,
                ReThrowDistributedCacheExceptions = false,
                ReThrowSerializationExceptions = false,
            });

        bool isLevel2Enabled = runtimeConfigAvailable
            && (runtimeConfig?.Runtime?.IsCachingEnabled ?? false)
            && (runtimeConfig?.Runtime?.Cache?.Level2?.Enabled ?? false);

        if (isLevel2Enabled)
        {
            RuntimeCacheLevel2Options level2CacheOptions = runtimeConfig!.Runtime!.Cache!.Level2!;
            string level2CacheProvider = level2CacheOptions.Provider ?? EntityCacheOptions.L2_CACHE_PROVIDER;

            if (level2CacheProvider.ToLowerInvariant() == EntityCacheOptions.L2_CACHE_PROVIDER)
            {
                if (string.IsNullOrWhiteSpace(level2CacheOptions.ConnectionString))
                {
                    throw new Exception($"Cache Provider: the \"{EntityCacheOptions.L2_CACHE_PROVIDER}\" level2 cache provider requires a valid connection-string.");
                }

                Task<ConnectionMultiplexer> connectionMultiplexerTask = ConnectionMultiplexer.ConnectAsync(level2CacheOptions.ConnectionString);
                fusionCacheBuilder
                    .WithSerializer(new FusionCacheSystemTextJsonSerializer())
                    .WithDistributedCache(new RedisCache(new RedisCacheOptions
                    {
                        ConnectionMultiplexerFactory = async () => await connectionMultiplexerTask
                    }))
                    .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
                    {
                        ConnectionMultiplexerFactory = async () => await connectionMultiplexerTask
                    }));
            }
        }

        services.AddSingleton<DabCacheService>();
    }

    private static async Task<bool> PerformOnConfigChangeAsync(WebApplication app)
    {
        try
        {
            Console.WriteLine("[StartupExtensions] PerformOnConfigChangeAsync started.");
            RuntimeConfigProvider runtimeConfigProvider = app.Services.GetService<RuntimeConfigProvider>()!;
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();
            Console.WriteLine($"[StartupExtensions] Runtime config retrieved. HostMode={runtimeConfig.Runtime?.Host?.Mode}.");

            RuntimeConfigValidator runtimeConfigValidator = app.Services.GetService<RuntimeConfigValidator>()!;
            runtimeConfigValidator.ValidateConfigProperties();

            if (runtimeConfig.IsDevelopmentMode())
            {
                runtimeConfigValidator.ValidatePermissionsInConfig(runtimeConfig);
                Console.WriteLine("[StartupExtensions] Permissions validated (development mode).");
            }

            // Debug: Check if ConnectionStrings__Database is set
            string? dbConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database");
            Console.WriteLine($"[StartupExtensions] DEBUG: ConnectionStrings__Database env var = {(string.IsNullOrEmpty(dbConnectionString) ? "(null or empty)" : dbConnectionString[..Math.Min(50, dbConnectionString.Length)] + "...")}");
            Console.WriteLine($"[StartupExtensions] DEBUG: Config data-source connection-string = {runtimeConfig.DataSource.ConnectionString?[..Math.Min(50, runtimeConfig.DataSource.ConnectionString.Length)] + "..."}");

            IMetadataProviderFactory sqlMetadataProviderFactory = app.Services.GetRequiredService<IMetadataProviderFactory>();
            await sqlMetadataProviderFactory.InitializeAsync();
            Console.WriteLine("[StartupExtensions] Metadata provider initialization complete.");

            GraphQLSchemaCreator graphQLSchemaCreator = app.Services.GetRequiredService<GraphQLSchemaCreator>();
            RestService restService = app.Services.GetRequiredService<RestService>();

            if (runtimeConfig.IsDevelopmentMode())
            {
                runtimeConfigValidator.ValidateRelationshipConfigCorrectness(runtimeConfig);
                runtimeConfigValidator.ValidateRelationships(runtimeConfig, sqlMetadataProviderFactory!);
            }

            if (!runtimeConfig.CosmosDataSourceUsed)
            {
                try
                {
                    IOpenApiDocumentor openApiDocumentor = app.Services.GetRequiredService<IOpenApiDocumentor>();
                    openApiDocumentor.CreateDocument(isHotReloadScenario: false);
                    Console.WriteLine("[StartupExtensions] OpenAPI document generation succeeded.");
                }
                catch (DataApiBuilderException dabException)
                {
                    Console.WriteLine($"[StartupExtensions] WARNING: OpenAPI document generation failed: {dabException.Message}.");
                }
            }

            Console.WriteLine("[StartupExtensions] Runtime initialization completed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StartupExtensions] ERROR: Runtime initialization failed. {ex.Message}");
            return false;
        }
    }
}
