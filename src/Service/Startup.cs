// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO.Abstractions;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Utilities;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Azure.DataApiBuilder.Core.AuthenticationHelpers.AuthenticationSimulator;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.HealthCheck;
using Azure.DataApiBuilder.Service.Telemetry;
using Azure.DataApiBuilder.Service.Utilities;
using Azure.Identity;
using Azure.Monitor.Ingestion;
using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
using CorsOptions = Azure.DataApiBuilder.Config.ObjectModel.CorsOptions;

namespace Azure.DataApiBuilder.Service
{
    public class Startup(IConfiguration configuration, ILogger<Startup> logger)
    {
        public static LogLevel MinimumLogLevel = LogLevel.Error;

        public static bool IsLogLevelOverriddenByCli;

        public static AzureLogAnalyticsCustomLogCollector CustomLogCollector = new();
        public static ApplicationInsightsOptions AppInsightsOptions = new();
        public static OpenTelemetryOptions OpenTelemetryOptions = new();
        public static AzureLogAnalyticsOptions AzureLogAnalyticsOptions = new();
        public static AzureLogAnalyticsFlusherService? FlusherService;
        public const string NO_HTTPS_REDIRECT_FLAG = "--no-https-redirect";
        private readonly HotReloadEventHandler<HotReloadEventArgs> _hotReloadEventHandler = new();
        private RuntimeConfigProvider? _configProvider;
        private ILogger<Startup> _logger = logger;

        public IConfiguration Configuration { get; } = configuration;

        /// <summary>
        /// Useful in cases where we need to:
        /// Send telemetry data to a custom endpoint that is not supported by the default telemetry channel.
        /// Modify the telemetry data before it is sent, such as adding custom properties or filtering out sensitive data.
        /// Implement custom retry logic or error handling for telemetry data that fails to send.
        /// For testing purposes.
        /// </summary>
        public static ITelemetryChannel? CustomTelemetryChannel { get; set; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            Startup.AddValidFilters();
            services.AddSingleton(_hotReloadEventHandler);
            string configFileName = Configuration.GetValue<string>("ConfigFileName") ?? FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME;
            string? connectionString = Configuration.GetValue<string?>(
                FileSystemRuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING.Replace(FileSystemRuntimeConfigLoader.ENVIRONMENT_PREFIX, ""),
                null);
            IFileSystem fileSystem = new FileSystem();
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem, _hotReloadEventHandler, configFileName, connectionString);
            RuntimeConfigProvider configProvider = new(configLoader);
            _configProvider = configProvider;

            services.AddSingleton(fileSystem);
            services.AddSingleton(configLoader);
            services.AddSingleton(configProvider);

            bool runtimeConfigAvailable = configProvider.TryGetConfig(out RuntimeConfig? runtimeConfig);

            if (runtimeConfigAvailable
                && runtimeConfig?.Runtime?.Telemetry?.ApplicationInsights is not null
                && runtimeConfig.Runtime.Telemetry.ApplicationInsights.Enabled)
            {
                // Add ApplicationTelemetry service and register
                // custom ITelemetryInitializer implementation with the dependency injection
                services.AddApplicationInsightsTelemetry();
                services.AddSingleton<ITelemetryInitializer, AppInsightsTelemetryInitializer>();
            }

            if (runtimeConfigAvailable
                && runtimeConfig?.Runtime?.Telemetry?.OpenTelemetry is not null
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
                        // TODO: should we also add FusionCache metrics?
                        // To do so we just need to add the package ZiggyCreatures.FusionCache.OpenTelemetry and call
                        // .AddFusionCacheInstrumentation()
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
                        // TODO: should we also add FusionCache traces?
                        // To do so we just need to add the package ZiggyCreatures.FusionCache.OpenTelemetry and call
                        // .AddFusionCacheInstrumentation()
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

            if (runtimeConfigAvailable
                && runtimeConfig?.Runtime?.Telemetry?.AzureLogAnalytics is not null
                && runtimeConfig.Runtime.Telemetry.AzureLogAnalytics.Enabled)
            {
                services.AddSingleton<ICustomLogCollector, AzureLogAnalyticsCustomLogCollector>();
                services.AddSingleton<ILoggerProvider, AzureLogAnalyticsLoggerProvider>();
                services.AddSingleton<AzureLogAnalyticsFlusherService>();
            }

            services.AddSingleton(implementationFactory: serviceProvider =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(RuntimeConfigValidator).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<RuntimeConfigValidator>();
            });
            services.AddSingleton<RuntimeConfigValidator>();

            services.AddSingleton<CosmosClientProvider>();
            services.AddHealthChecks()
                .AddCheck<BasicHealthCheck>(nameof(BasicHealthCheck));

            services.AddSingleton<ILogger<SqlQueryEngine>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(SqlQueryEngine).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<SqlQueryEngine>();
            });

            services.AddSingleton<ILogger<IQueryExecutor>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(IQueryExecutor).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<IQueryExecutor>();
            });

            services.AddSingleton<ILogger<ISqlMetadataProvider>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(ISqlMetadataProvider).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<ISqlMetadataProvider>();
            });

            // Below are the factory registrations that will enable multiple databases scenario.
            // within these factories the various instances will be created based on the database type and datasourceName.
            services.AddSingleton<IAbstractQueryManagerFactory, QueryManagerFactory>();

            services.AddSingleton<IQueryEngineFactory, QueryEngineFactory>();

            services.AddSingleton<IMutationEngineFactory, MutationEngineFactory>();

            services.AddSingleton<IMetadataProviderFactory, MetadataProviderFactory>();

            services.AddSingleton<GraphQLSchemaCreator>();
            services.AddSingleton<GQLFilterParser>();
            services.AddSingleton<RequestValidator>();
            services.AddSingleton<RestService>();
            services.AddSingleton<HealthCheckHelper>();
            services.AddSingleton<HttpUtilities>();
            services.AddSingleton<BasicHealthReportResponseWriter>();
            services.AddSingleton<ComprehensiveHealthReportResponseWriter>();

            // ILogger explicit creation required for logger to use --LogLevel startup argument specified.
            services.AddSingleton<ILogger<BasicHealthReportResponseWriter>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(BasicHealthReportResponseWriter).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<BasicHealthReportResponseWriter>();
            });

            // ILogger explicit creation required for logger to use --LogLevel startup argument specified.
            services.AddSingleton<ILogger<ComprehensiveHealthReportResponseWriter>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(ComprehensiveHealthReportResponseWriter).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<ComprehensiveHealthReportResponseWriter>();
            });

            // ILogger explicit creation required for logger to use --LogLevel startup argument specified.
            services.AddSingleton<ILogger<HealthCheckHelper>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(HealthCheckHelper).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<HealthCheckHelper>();
            });

            // ILogger explicit creation required for logger to use --LogLevel startup argument specified.
            services.AddSingleton<ILogger<HttpUtilities>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(HttpUtilities).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<HttpUtilities>();
            });

            services.AddSingleton<ILogger<RestController>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(RestController).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<RestController>();
            });

            services.AddSingleton<ILogger<ClientRoleHeaderAuthenticationMiddleware>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelRuntime = new(MinimumLogLevel, typeof(ClientRoleHeaderAuthenticationMiddleware).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelRuntime);
                return loggerFactory.CreateLogger<ClientRoleHeaderAuthenticationMiddleware>();
            });

            services.AddSingleton<ILogger<ConfigurationController>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(ConfigurationController).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<ConfigurationController>();
            });

            //Enable accessing HttpContext in RestService to get ClaimsPrincipal.
            services.AddHttpContextAccessor();

            services.AddHttpClient("ContextConfiguredHealthCheckClient")
                .ConfigureHttpClient((serviceProvider, client) =>
                {
                    int port = PortResolutionHelper.ResolveInternalPort();
                    string baseUri = $"http://localhost:{port}";
                    client.BaseAddress = new Uri(baseUri);
                    _logger.LogInformation($"Configured HealthCheck HttpClient BaseAddress as: {baseUri}");
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    client.Timeout = TimeSpan.FromSeconds(200);
                })
                .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
                {
                    // For debug purpose, USE_SELF_SIGNED_CERT can be set to true in Environment variables
                    bool allowSelfSigned = Environment.GetEnvironmentVariable("USE_SELF_SIGNED_CERT")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

                    HttpClientHandler handler = new();

                    if (allowSelfSigned)
                    {
                        handler.ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                    }

                    return handler;
                });

            if (runtimeConfig is not null && runtimeConfig.Runtime?.Host?.Mode is HostMode.Development)
            {
                // Development mode implies support for "Hot Reload". The V2 authentication function
                // wires up all DAB supported authentication providers (schemes) so that at request time,
                // the runtime config defined authentication provider is used to authenticate requests.
                ConfigureAuthenticationV2(services, configProvider);
            }
            else
            {
                ConfigureAuthentication(services, configProvider);
            }

            services.AddAuthorization();
            services.AddSingleton<ILogger<IAuthorizationHandler>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(IAuthorizationHandler).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<IAuthorizationHandler>();
            });
            services.AddSingleton<ILogger<IAuthorizationResolver>>(implementationFactory: (serviceProvider) =>
            {
                LogLevelInitializer logLevelInit = new(MinimumLogLevel, typeof(IAuthorizationResolver).FullName, _configProvider, _hotReloadEventHandler);
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider, logLevelInit);
                return loggerFactory.CreateLogger<IAuthorizationResolver>();
            });

            services.AddSingleton<IAuthorizationHandler, RestAuthorizationHandler>();
            services.AddSingleton<IAuthorizationResolver, AuthorizationResolver>();
            services.AddSingleton<IOpenApiDocumentor, OpenApiDocumentor>();

            AddGraphQLService(services, runtimeConfig?.Runtime?.GraphQL);

            // Subscribe the GraphQL schema refresh method to the specific hot-reload event
            _hotReloadEventHandler.Subscribe(
                DabConfigEvents.GRAPHQL_SCHEMA_REFRESH_ON_CONFIG_CHANGED,
                (_, _) => RefreshGraphQLSchema(services));

            // Cache config
            IFusionCacheBuilder fusionCacheBuilder = services.AddFusionCache()
                .WithOptions(options =>
                {
                    options.FactoryErrorsLogLevel = LogLevel.Debug;
                    options.EventHandlingErrorsLogLevel = LogLevel.Debug;
                    string? cachePartition = runtimeConfig?.Runtime?.Cache?.Level2?.Partition;
                    if (string.IsNullOrWhiteSpace(cachePartition) == false)
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

            // Level2 cache config
            bool isLevel2Enabled = runtimeConfigAvailable
                && (runtimeConfig?.Runtime?.IsCachingEnabled ?? false)
                && (runtimeConfig?.Runtime?.Cache?.Level2?.Enabled ?? false);

            if (isLevel2Enabled)
            {
                RuntimeCacheLevel2Options level2CacheOptions = runtimeConfig!.Runtime!.Cache!.Level2!;
                string level2CacheProvider = level2CacheOptions.Provider ?? EntityCacheOptions.L2_CACHE_PROVIDER;

                switch (level2CacheProvider.ToLowerInvariant())
                {
                    case EntityCacheOptions.L2_CACHE_PROVIDER:
                        if (string.IsNullOrWhiteSpace(level2CacheOptions.ConnectionString))
                        {
                            throw new Exception($"Cache Provider: the \"{EntityCacheOptions.L2_CACHE_PROVIDER}\" level2 cache provider requires a valid connection-string. Please provide one.");
                        }
                        else
                        {
                            // NOTE: this is done to reuse the same connection multiplexer for both the cache and backplane
                            Task<ConnectionMultiplexer> connectionMultiplexerTask = ConnectionMultiplexer.ConnectAsync(level2CacheOptions.ConnectionString);

                            fusionCacheBuilder
                                .WithSerializer(new FusionCacheSystemTextJsonSerializer())
                                .WithDistributedCache(new RedisCache(new RedisCacheOptions
                                {
                                    ConnectionMultiplexerFactory = async () =>
                                    {
                                        return await connectionMultiplexerTask;
                                    }
                                }))
                                .WithBackplane(new RedisBackplane(new RedisBackplaneOptions
                                {
                                    ConnectionMultiplexerFactory = async () =>
                                    {
                                        return await connectionMultiplexerTask;
                                    }
                                }));
                        }

                        break;
                    default:
                        throw new Exception($"Cache Provider: ${level2CacheOptions.Provider} not supported. Please provide a valid cache provider.");
                }
            }

            services.AddSingleton<DabCacheService>();
            services.AddControllers();
        }

        /// <summary>
        /// Configure GraphQL services within the service collection of the
        /// request pipeline.
        /// - AllowIntrospection defaulted to false so HotChocolate configures a request validation rule
        /// that checks for the presence of the GraphQL context key WellKnownContextData.IntrospectionAllowed
        /// when determining whether to allow introspection requests to proceed.
        /// </summary>
        /// <param name="services">Service Collection</param>
        /// <param name="graphQLRuntimeOptions">The GraphQL runtime options.</param>
        private void AddGraphQLService(IServiceCollection services, GraphQLRuntimeOptions? graphQLRuntimeOptions)
        {
            IRequestExecutorBuilder server = services.AddGraphQLServer()
                .AddInstrumentation()
                .AddType(new DateTimeType(disableFormatCheck: graphQLRuntimeOptions?.EnableLegacyDateTimeScalar ?? true))
                .AddHttpRequestInterceptor<DefaultHttpRequestInterceptor>()
                .ConfigureSchema((serviceProvider, schemaBuilder) =>
                {
                    GraphQLSchemaCreator graphQLService = serviceProvider.GetRequiredService<GraphQLSchemaCreator>();
                    graphQLService.InitializeSchemaAndResolvers(schemaBuilder);
                })
                .AddHttpRequestInterceptor<IntrospectionInterceptor>()
                .AddAuthorizationHandler<GraphQLAuthorizationHandler>()
                .BindRuntimeType<TimeOnly, HotChocolate.Types.NodaTime.LocalTimeType>()
                .BindScalarType<HotChocolate.Types.NodaTime.LocalTimeType>("LocalTime")
                .AddTypeConverter<LocalTime, TimeOnly>(
                    from => new TimeOnly(from.Hour, from.Minute, from.Second, from.Millisecond))
                .AddTypeConverter<TimeOnly, LocalTime>(
                    from => new LocalTime(from.Hour, from.Minute, from.Second, from.Millisecond));

            // Conditionally adds a maximum depth rule to the GraphQL queries/mutation selection set.
            // This rule is only added if a positive depth limit is specified, ensuring that the server
            // enforces a limit on the depth of incoming GraphQL queries/mutation to prevent extremely deep queries
            // that could potentially lead to performance issues.
            // Additionally, the skipIntrospectionFields parameter is set to true to skip depth limit enforcement on introspection queries.
            if (graphQLRuntimeOptions is not null && graphQLRuntimeOptions.DepthLimit is > 0)
            {
                server = server.AddMaxExecutionDepthRule(maxAllowedExecutionDepth: graphQLRuntimeOptions.DepthLimit.Value, skipIntrospectionFields: true);
            }

            server.AddErrorFilter(error =>
            {
                if (error.Exception is not null)
                {
                    _logger.LogError(exception: error.Exception, message: "A GraphQL request execution error occurred.");
                    return error.WithMessage(error.Exception.Message);
                }

                if (error.Code is not null)
                {
                    _logger.LogError(message: "Error code: {errorCode}\nError message: {errorMessage}", error.Code, error.Message);
                    return error.WithMessage(error.Message);
                }

                return error;
            })
                .AddErrorFilter(error =>
                {
                    if (error.Exception is DataApiBuilderException thrownException)
                    {
                        error = error
                            .WithException(null)
                            .WithMessage(thrownException.Message)
                            .WithCode($"{thrownException.SubStatusCode}");

                        // If user error i.e. validation error or conflict error with datasource, then retain location/path
                        if (!thrownException.StatusCode.IsClientError())
                        {
                            error = error.WithLocations(Array.Empty<Location>());
                        }
                    }

                    return error;
                })
                // Allows DAB to override the HTTP error code set by HotChocolate.
                // This is used to ensure HTTP code 4XX is set when the datatbase
                // returns a "bad request" error such as stored procedure params missing.
                .UseRequest<DetermineStatusCodeMiddleware>()
                .UseRequest<BuildRequestStateMiddleware>()
                .UseDefaultPipeline();
        }

        /// <summary>
        /// Refreshes the GraphQL schema when the runtime config updates during hot-reload scenario.
        /// </summary>
        private void RefreshGraphQLSchema(IServiceCollection services)
        {
            // Re-add GraphQL services with updated config.
            RuntimeConfig runtimeConfig = _configProvider!.GetConfig();
            Console.WriteLine("Updating GraphQL service.");
            AddGraphQLService(services, runtimeConfig.Runtime?.GraphQL);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, RuntimeConfigProvider runtimeConfigProvider, IHostApplicationLifetime hostLifetime)
        {
            bool isRuntimeReady = false;

            if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                // Configure Application Insights Telemetry
                ConfigureApplicationInsightsTelemetry(app, runtimeConfig);
                ConfigureOpenTelemetry(runtimeConfig);
                ConfigureAzureLogAnalytics(runtimeConfig);

                // Config provided before starting the engine.
                isRuntimeReady = PerformOnConfigChangeAsync(app).Result;

                if (!isRuntimeReady)
                {
                    _logger.LogError(
                        message: "Could not initialize the engine with the runtime config file: {configFilePath}",
                        runtimeConfigProvider.ConfigFilePath);
                    hostLifetime.StopApplication();
                }
            }
            else
            {
                // Config provided during runtime.
                runtimeConfigProvider.IsLateConfigured = true;
                runtimeConfigProvider.RuntimeConfigLoadedHandlers.Add(async (_, _) =>
                {
                    isRuntimeReady = await PerformOnConfigChangeAsync(app);
                    return isRuntimeReady;
                });
            }

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            if (!Program.IsHttpsRedirectionDisabled)
            {
                // Use HTTPS redirection for all endpoints except /health and /graphql.
                // This is necessary because ContextConfiguredHealthCheckClient base URI is http://localhost:{port} for internal API calls
                app.UseWhen(
                    context => !(context.Request.Path.StartsWithSegments("/health") || context.Request.Path.StartsWithSegments("/graphql")),
                    appBuilder => appBuilder.UseHttpsRedirection()
                );
            }

            // URL Rewrite middleware MUST be called prior to UseRouting().
            // https://andrewlock.net/understanding-pathbase-in-aspnetcore/#placing-usepathbase-in-the-correct-location
            app.UseCorrelationIdMiddleware();
            app.UsePathRewriteMiddleware();

            // SwaggerUI visualization of the OpenAPI description document is only available
            // in developer mode in alignment with the restriction placed on ChilliCream's BananaCakePop IDE.
            // Consequently, SwaggerUI is not presented in a StaticWebApps (late-bound config) environment.
            if (IsUIEnabled(runtimeConfig, env))
            {
                app.UseSwaggerUI(c => // CodeQL [SM04686] SwaggerUI is only enabled for Development environment.
                {
                    c.ConfigObject.Urls = new SwaggerEndpointMapper(app.ApplicationServices.GetService<RuntimeConfigProvider?>());
                });
            }

            app.UseRouting();

            // Adding CORS Middleware
            if (runtimeConfig is not null && runtimeConfig.Runtime?.Host?.Cors is not null)
            {
                app.UseCors(corsPolicyBuilder =>
                {
                    CorsOptions corsConfig = runtimeConfig.Runtime.Host.Cors;
                    ConfigureCors(corsPolicyBuilder, corsConfig);
                });
            }

            app.Use(async (context, next) =>
            {
                bool isHealthCheckRequest = context.Request.Path == "/" && context.Request.Method == HttpMethod.Get.Method;
                bool isSettingConfig = context.Request.Path.StartsWithSegments("/configuration")
                    && context.Request.Method == HttpMethod.Post.Method;
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

            // Authorization Engine middleware enforces that all requests (including introspection)
            // include proper auth headers.
            // - {Authorization header + Client role header for JWT}
            // - {X-MS-CLIENT-PRINCIPAL + Client role header for EasyAuth}
            // When enabled, the middleware will prevent Banana Cake Pop(GraphQL client) from loading
            // without proper authorization headers.
            app.UseClientRoleHeaderAuthorizationMiddleware();

            IRequestExecutorResolver requestExecutorResolver = app.ApplicationServices.GetRequiredService<IRequestExecutorResolver>();
            _hotReloadEventHandler.Subscribe(
                "GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED",
                (_, _) => EvictGraphQLSchema(requestExecutorResolver));

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();

                endpoints
                    .MapGraphQL()
                    .WithOptions(new GraphQLServerOptions
                    {
                        Tool = {
                            // Determines if accessing the endpoint from a browser
                            // will load the GraphQL Banana Cake Pop IDE.
                            Enable = IsUIEnabled(runtimeConfig, env)
                        }
                    });

                // In development mode, Nitro is enabled at /graphql endpoint by default.
                // Need to disable mapping Nitro explicitly as well to avoid ability to query
                // at an additional endpoint: /graphql/ui.
                endpoints
                    .MapNitroApp()
                    .WithOptions(new GraphQLToolOptions
                    {
                        Enable = false
                    });

                endpoints.MapHealthChecks("/", new HealthCheckOptions
                {
                    ResponseWriter = app.ApplicationServices.GetRequiredService<BasicHealthReportResponseWriter>().WriteResponse
                });
            });
        }

        /// <summary>
        /// Evicts the GraphQL schema from the request executor resolver.
        /// </summary>
        private static void EvictGraphQLSchema(IRequestExecutorResolver requestExecutorResolver)
        {
            Console.WriteLine("Evicting old GraphQL schema.");
            requestExecutorResolver.EvictRequestExecutor();
        }

        /// <summary>
        /// If LogLevel is NOT overridden by CLI, attempts to find the
        /// minimum log level based on host.mode in the runtime config if available.
        /// Creates a logger factory with the minimum log level.
        /// </summary>
        public static ILoggerFactory CreateLoggerFactoryForHostedAndNonHostedScenario(IServiceProvider serviceProvider, LogLevelInitializer logLevelInitializer)
        {
            if (!IsLogLevelOverriddenByCli)
            {
                // If the log level is not overridden by command line arguments specified through CLI,
                // attempt to get the runtime config to determine the loglevel based on host.mode.
                // If runtime config is available, set the loglevel to Error if host.mode is Production,
                // Debug if it is Development.

                logLevelInitializer.SetLogLevel();
            }

            TelemetryClient? appTelemetryClient = serviceProvider.GetService<TelemetryClient>();

            return Program.GetLoggerFactoryForLogLevel(logLevelInitializer.MinLogLevel, appTelemetryClient, logLevelInitializer);
        }

        /// <summary>
        /// Add services necessary for Authentication Middleware and based on the loaded
        /// runtime configuration set the AuthenticationOptions to be either
        /// EasyAuth based (by default) or JwtBearerOptions.
        /// When no runtime configuration is set on engine startup, set the
        /// default authentication scheme to EasyAuth.
        /// </summary>
        /// <param name="services">The service collection where authentication services are added.</param>
        /// <param name="runtimeConfigurationProvider">The provider used to load runtime configuration.</param>
        private void ConfigureAuthentication(IServiceCollection services, RuntimeConfigProvider runtimeConfigurationProvider)
        {
            if (runtimeConfigurationProvider.TryGetConfig(out RuntimeConfig? runtimeConfig) &&
                runtimeConfig.Runtime?.Host?.Authentication is not null)
            {
                AuthenticationOptions authOptions = runtimeConfig.Runtime.Host.Authentication;
                HostMode mode = runtimeConfig.Runtime.Host.Mode;
                if (!authOptions.IsAuthenticationSimulatorEnabled() && !authOptions.IsEasyAuthAuthenticationProvider())
                {
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.MapInboundClaims = false;
                        options.Audience = authOptions.Jwt!.Audience;
                        options.Authority = authOptions.Jwt!.Issuer;
                        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
                        {
                            // Instructs the asp.net core middleware to use the data in the "roles" claim for User.IsInRole()
                            // See https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal.isinrole#remarks
                            RoleClaimType = AuthenticationOptions.ROLE_CLAIM_TYPE
                        };
                    });
                }
                else if (authOptions.IsEasyAuthAuthenticationProvider())
                {
                    EasyAuthType easyAuthType = EnumExtensions.Deserialize<EasyAuthType>(runtimeConfig.Runtime.Host.Authentication.Provider);
                    bool isProductionMode = mode != HostMode.Development;
                    bool appServiceEnvironmentDetected = AppServiceAuthenticationInfo.AreExpectedAppServiceEnvVarsPresent();

                    if (easyAuthType == EasyAuthType.AppService && !appServiceEnvironmentDetected)
                    {
                        if (isProductionMode)
                        {
                            throw new DataApiBuilderException(
                                message: AppServiceAuthenticationInfo.APPSERVICE_PROD_MISSING_ENV_CONFIG,
                                statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                        }
                        else
                        {
                            _logger.LogWarning(AppServiceAuthenticationInfo.APPSERVICE_DEV_MISSING_ENV_CONFIG);
                        }
                    }

                    services.AddAuthentication(EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME)
                        .AddEasyAuthAuthentication(easyAuthAuthenticationProvider: easyAuthType);
                }
                else if (mode == HostMode.Development && authOptions.IsAuthenticationSimulatorEnabled())
                {
                    services.AddAuthentication(SimulatorAuthenticationDefaults.AUTHENTICATIONSCHEME)
                        .AddSimulatorAuthentication();
                }
                else
                {
                    // Condition met when Jwt section (audience/authority), EasyAuth types, or Simulator (in development mode)
                    // values are not used in the authentication section.
                    throw new DataApiBuilderException(
                        message: "Authentication configuration not supported.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                }
            }
            else
            {
                // Sets EasyAuth as the default authentication scheme when runtime configuration
                // is not present.
                SetStaticWebAppsAuthentication(services);
            }
        }

        /// <summary>
        /// Registers all DAB supported authentication providers (schemes) so that at request time,
        /// DAB can use the runtime config's defined provider to authenticate requests.
        /// The function includes JWT specific configuration handling:
        /// - IOptionsChangeTokenSource{JwtBearerOptions} : Registers a change token source for dynamic config updates which
        /// is used internally by JwtBearerHandler's OptionsMonitor to listen for changes in JwtBearerOptions.
        /// - IConfigureOptions{JwtBearerOptions} : Registers named JwtBearerOptions whose "Configure(...)" function is
        /// called by OptionsFactory internally by .NET to fetch the latest configuration from the RuntimeConfigProvider.
        /// </summary>
        /// <seealso cref="https://github.com/dotnet/aspnetcore/issues/49586#issuecomment-1671838595">Guidance for registering IOptionsChangeTokenSource</seealso>
        /// <seealso cref="https://github.com/dotnet/aspnetcore/issues/21491#issuecomment-624240160">Guidance for registering named options.</seealso>
        private static void ConfigureAuthenticationV2(IServiceCollection services, RuntimeConfigProvider runtimeConfigProvider)
        {
            services.AddSingleton<IOptionsChangeTokenSource<JwtBearerOptions>>(new JwtBearerOptionsChangeTokenSource(runtimeConfigProvider));
            services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
            services.AddAuthentication()
                    .AddEnvDetectedEasyAuth()
                    .AddJwtBearer()
                    .AddSimulatorAuthentication();
        }

        /// <summary>
        /// Configure Application Insights Telemetry based on the loaded runtime configuration. If Application Insights
        /// is enabled, we can track different events and metrics.
        /// </summary>
        /// <param name="app">The application builder.</param>
        /// <param name="runtimeConfig">The provider used to load runtime configuration.</param>
        /// <seealso cref="https://docs.microsoft.com/en-us/azure/azure-monitor/app/asp-net-core#enable-application-insights-telemetry-collection"/>
        private void ConfigureApplicationInsightsTelemetry(IApplicationBuilder app, RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig?.Runtime?.Telemetry is not null
                && runtimeConfig.Runtime.Telemetry.ApplicationInsights is not null)
            {
                AppInsightsOptions = runtimeConfig.Runtime.Telemetry.ApplicationInsights;

                if (!AppInsightsOptions.Enabled)
                {
                    _logger.LogInformation("Application Insights are disabled.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(AppInsightsOptions.ConnectionString))
                {
                    _logger.LogWarning("Logs won't be sent to Application Insights because an Application Insights connection string is not available in the runtime config.");
                    return;
                }

                TelemetryClient? appTelemetryClient = app.ApplicationServices.GetService<TelemetryClient>();

                if (appTelemetryClient is null)
                {
                    _logger.LogError("Telemetry client is not initialized.");
                    return;
                }

                // Update the TelemetryConfiguration object
                TelemetryConfiguration telemetryConfiguration = appTelemetryClient.TelemetryConfiguration;
                telemetryConfiguration.ConnectionString = AppInsightsOptions.ConnectionString;

                // Update default telemetry channel to custom provided telemetry channel
                if (CustomTelemetryChannel is not null)
                {
                    telemetryConfiguration.TelemetryChannel = CustomTelemetryChannel;
                }

                // Updating Startup Logger to Log from Startup Class.
                ILoggerFactory loggerFactory = Program.GetLoggerFactoryForLogLevel(MinimumLogLevel, appTelemetryClient);
                _logger = loggerFactory.CreateLogger<Startup>();
            }
        }

        /// <summary>
        /// Configure Open Telemetry based on the loaded runtime configuration. If Open Telemetry
        /// is enabled, we can track different events and metrics.
        /// </summary>
        /// <param name="runtimeConfigurationProvider">The provider used to load runtime configuration.</param>
        private void ConfigureOpenTelemetry(RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig?.Runtime?.Telemetry is not null
                && runtimeConfig.Runtime.Telemetry.OpenTelemetry is not null)
            {
                OpenTelemetryOptions = runtimeConfig.Runtime.Telemetry.OpenTelemetry;

                if (!OpenTelemetryOptions.Enabled)
                {
                    _logger.LogInformation("Open Telemetry is disabled.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(OpenTelemetryOptions?.Endpoint))
                {
                    _logger.LogWarning("Logs won't be sent to Open Telemetry because an Open Telemetry connection string is not available in the runtime config.");
                    return;
                }

                // Updating Startup Logger to Log from Startup Class.
                ILoggerFactory? loggerFactory = Program.GetLoggerFactoryForLogLevel(MinimumLogLevel);
                _logger = loggerFactory.CreateLogger<Startup>();
            }
        }

        /// <summary>
        /// Configure Azure Log Analytics based on the loaded runtime configuration. If Azure Log Analytics
        /// is enabled, we can track different events and metrics.
        /// </summary>
        /// <param name="runtimeConfigurationProvider">The provider used to load runtime configuration.</param>
        private void ConfigureAzureLogAnalytics(RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig?.Runtime?.Telemetry is not null
                && runtimeConfig.Runtime.Telemetry.AzureLogAnalytics is not null)
            {
                AzureLogAnalyticsOptions = runtimeConfig.Runtime.Telemetry.AzureLogAnalytics;

                if (!AzureLogAnalyticsOptions.Enabled)
                {
                    _logger.LogInformation("Azure Log Analytics is disabled.");
                    return;
                }

                if (string.IsNullOrEmpty(AzureLogAnalyticsOptions.Auth?.CustomTableName))
                {
                    _logger.LogInformation("Logs won't be sent to Azure Log Analytics because the Custom Table Name is not available in the config file.");
                    return;
                }

                if (string.IsNullOrEmpty(AzureLogAnalyticsOptions.Auth?.DcrImmutableId))
                {
                    _logger.LogInformation("Logs won't be sent to Azure Log Analytics because the DCR Immutable Id is not available in the config file.");
                    return;
                }

                if (string.IsNullOrEmpty(AzureLogAnalyticsOptions.Auth?.DceEndpoint))
                {
                    _logger.LogInformation("Logs won't be sent to Azure Log Analytics because the DCE Endpoint is not available in the config file.");
                    return;
                }

                DefaultAzureCredential credential = new();
                LogsIngestionClient logsIngestionClient = new(new Uri(AzureLogAnalyticsOptions.Auth.DceEndpoint), credential);
                FlusherService = new AzureLogAnalyticsFlusherService(AzureLogAnalyticsOptions, CustomLogCollector, logsIngestionClient);

                // Updating Startup Logger to Log from Startup Class.
                ILoggerFactory? loggerFactory = Program.GetLoggerFactoryForLogLevel(MinimumLogLevel);
                _logger = loggerFactory.CreateLogger<Startup>();
                _ = Task.Run(() => FlusherService.StartAsync());
            }
        }

        /// <summary>
        /// Sets Static Web Apps EasyAuth as the authentication scheme for the engine.
        /// </summary>
        /// <param name="services">The service collection where authentication services are added.</param>
        private static void SetStaticWebAppsAuthentication(IServiceCollection services)
        {
            services.AddAuthentication(EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME)
                    .AddEasyAuthAuthentication(EasyAuthType.StaticWebApps);
        }

        /// <summary>
        /// Perform these additional steps once the configuration has been bound
        /// to a particular database type.
        /// </summary>
        /// <param name="app"></param>
        /// <returns>Indicates if the runtime is ready to accept requests.</returns>
        private async Task<bool> PerformOnConfigChangeAsync(IApplicationBuilder app)
        {
            try
            {
                RuntimeConfigProvider runtimeConfigProvider = app.ApplicationServices.GetService<RuntimeConfigProvider>()!;
                RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

                RuntimeConfigValidator runtimeConfigValidator = app.ApplicationServices.GetService<RuntimeConfigValidator>()!;
                // Now that the configuration has been set, perform validation of the runtime config
                // itself.

                runtimeConfigValidator.ValidateConfigProperties();

                if (runtimeConfig.IsDevelopmentMode())
                {
                    // Running only in developer mode to ensure fast and smooth startup in production.
                    runtimeConfigValidator.ValidatePermissionsInConfig(runtimeConfig);
                }

                IMetadataProviderFactory sqlMetadataProviderFactory =
                    app.ApplicationServices.GetRequiredService<IMetadataProviderFactory>();
                await sqlMetadataProviderFactory.InitializeAsync();

                // Manually trigger DI service instantiation of GraphQLSchemaCreator and RestService
                // to attempt to reduce chances that the first received client request
                // triggers instantiation and encounters undesired instantiation latency.
                // In their constructors, those services consequentially inject
                // other required services, triggering instantiation. Such recursive nature of DI and
                // service instantiation results in the activation of all required services.
                GraphQLSchemaCreator graphQLSchemaCreator =
                    app.ApplicationServices.GetRequiredService<GraphQLSchemaCreator>();

                RestService restService =
                    app.ApplicationServices.GetRequiredService<RestService>();

                if (graphQLSchemaCreator is null || restService is null)
                {
                    _logger.LogError("Endpoint service initialization failed.");
                }

                if (runtimeConfig.IsDevelopmentMode())
                {
                    // Running only in developer mode to ensure fast and smooth startup in production.
                    runtimeConfigValidator.ValidateRelationshipConfigCorrectness(runtimeConfig);
                    runtimeConfigValidator.ValidateRelationships(runtimeConfig, sqlMetadataProviderFactory!);
                }

                // OpenAPI document creation is only attempted for REST supporting database types.
                // CosmosDB is not supported for OpenAPI document creation.
                if (!runtimeConfig.CosmosDataSourceUsed)
                {
                    // Attempt to create OpenAPI document.
                    // Errors must not crash nor halt the intialization of the engine
                    // because OpenAPI document creation is not required for the engine to operate.
                    // Errors will be logged.
                    try
                    {
                        IOpenApiDocumentor openApiDocumentor = app.ApplicationServices.GetRequiredService<IOpenApiDocumentor>();
                        openApiDocumentor.CreateDocument(isHotReloadScenario: false);
                    }
                    catch (DataApiBuilderException dabException)
                    {
                        _logger.LogWarning(exception: dabException, message: "OpenAPI Documentor initialization failed. This will not affect dab startup.");
                    }
                }

                _logger.LogInformation("Successfully completed runtime initialization.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(exception: ex, message: "Unable to complete runtime initialization. Refer to exception for error details.");
                return false;
            }
        }

        /// <summary>
        /// Build a CorsPolicy to be consumed by the useCors function, allowing requests with any methods or headers
        /// Used both for app startup and testing purposes
        /// </summary>
        /// <param name="builder"> The CorsPolicyBuilder that will be used to build the policy </param>
        /// <param name="corsConfig"> The cors runtime configuration specifying the allowed origins and whether credentials can be included in requests </param>
        /// <returns> The built cors policy </returns>
        public static CorsPolicy ConfigureCors(CorsPolicyBuilder builder, CorsOptions corsConfig)
        {
            if (corsConfig.AllowCredentials)
            {
                return builder
                    .WithOrigins(corsConfig.Origins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowCredentials()
                    .Build();
            }

            return builder
                .WithOrigins(corsConfig.Origins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .SetIsOriginAllowedToAllowWildcardSubdomains()
                .Build();
        }

        /// <summary>
        /// Indicates whether to provide UI visualization of REST(via Swagger) or GraphQL (via Banana CakePop).
        /// </summary>
        private static bool IsUIEnabled(RuntimeConfig? runtimeConfig, IWebHostEnvironment env)
        {
            return (runtimeConfig is not null && runtimeConfig.IsDevelopmentMode()) || env.IsDevelopment();
        }

        /// <summary>
        /// Adds all of the class namespaces that have loggers that the user is able to change
        /// </summary>
        public static void AddValidFilters()
        {
            LoggerFilters.AddFilter(typeof(RuntimeConfigValidator).FullName);
            LoggerFilters.AddFilter(typeof(SqlQueryEngine).FullName);
            LoggerFilters.AddFilter(typeof(IQueryExecutor).FullName);
            LoggerFilters.AddFilter(typeof(ISqlMetadataProvider).FullName);
            LoggerFilters.AddFilter(typeof(BasicHealthReportResponseWriter).FullName);
            LoggerFilters.AddFilter(typeof(ComprehensiveHealthReportResponseWriter).FullName);
            LoggerFilters.AddFilter(typeof(RestController).FullName);
            LoggerFilters.AddFilter(typeof(ClientRoleHeaderAuthenticationMiddleware).FullName);
            LoggerFilters.AddFilter(typeof(ConfigurationController).FullName);
            LoggerFilters.AddFilter(typeof(IAuthorizationHandler).FullName);
            LoggerFilters.AddFilter(typeof(IAuthorizationResolver).FullName);
            LoggerFilters.AddFilter("default");
        }
    }
}
