// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CorrelationIdMiddleware = Azure.DataApiBuilder.Core.Services.CorrelationIdMiddleware;
using PathRewriteMiddleware = Azure.DataApiBuilder.Core.Services.PathRewriteMiddleware;
using SwaggerEndpointMapper = Azure.DataApiBuilder.Core.Services.OpenAPI.SwaggerEndpointMapper;

namespace Azure.DataApiBuilder.Service
{
    /// <summary>
    /// Legacy Startup class maintained for backward compatibility with tests.
    /// New applications should use the minimal hosting model in Program.cs.
    /// </summary>
    [Obsolete("This class is maintained for backward compatibility with tests. Use Program.CreateWebApplicationBuilder() for new code.")]
    public class Startup
    {
        private readonly ILogger<Startup> _logger;
        private readonly HotReloadEventHandler<HotReloadEventArgs> _hotReloadEventHandler;

        public Startup(IConfiguration configuration, ILogger<Startup> logger)
        {
            Configuration = configuration;
            _logger = logger;
            _hotReloadEventHandler = new HotReloadEventHandler<HotReloadEventArgs>();
            _logger.LogInformation("Using legacy Startup class. Consider migrating to minimal hosting model.");
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            _logger.LogInformation("Configuring services via legacy Startup class.");
            
            // Register the hot reload event handler first
            services.AddSingleton(_hotReloadEventHandler);
            
            // Use the extension method to configure services
            //services.ConfigureServices(Configuration);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            _logger.LogInformation("Configuring pipeline via legacy Startup class for environment {EnvironmentName}.", env.EnvironmentName);
            
            // Use the legacy pipeline configuration helper
            ConfigureLegacyPipeline(app, env).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Configures the HTTP request pipeline using the legacy IApplicationBuilder pattern.
        /// This method replicates the logic from StartupExtensions.ConfigurePipeline() but works
        /// with IApplicationBuilder instead of WebApplication for test compatibility.
        /// </summary>
        private async Task ConfigureLegacyPipeline(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var runtimeConfigProvider = app.ApplicationServices.GetRequiredService<RuntimeConfigProvider>();
            var hostLifetime = app.ApplicationServices.GetService<IHostApplicationLifetime>();
            
            _logger.LogInformation("Configuring HTTP pipeline for environment {EnvironmentName}.", env.EnvironmentName);

            bool isRuntimeReady = false;

            if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                _logger.LogInformation("Runtime config already loaded. Initializing services with pre-loaded configuration.");

                // Configure telemetry
                if (runtimeConfig is not null)
                {
                    ConfigureTelemetryInLegacyMode(app, runtimeConfig);
                }

                // Config provided before starting the engine.
                _logger.LogInformation("Triggering engine initialization prior to request pipeline start.");
                isRuntimeReady = await PerformOnConfigChangeAsync(app);
                _logger.LogInformation("Engine initialization completed with status {IsRuntimeReady}.", isRuntimeReady);

                if (!isRuntimeReady)
                {
                    _logger.LogError("Could not initialize the engine with the runtime config file: {configFilePath}", runtimeConfigProvider.ConfigFilePath);
                    hostLifetime?.StopApplication();
                }
            }
            else
            {
                // Config provided during runtime.
                _logger.LogWarning("Runtime config not available during startup. Waiting for late-bound configuration.");
                runtimeConfigProvider.IsLateConfigured = true;
                runtimeConfigProvider.RuntimeConfigLoadedHandlers.Add(async (_, _) =>
                {
                    _logger.LogInformation("Late-bound runtime config detected. Re-initializing engine.");
                    isRuntimeReady = await PerformOnConfigChangeAsync(app);
                    _logger.LogInformation("Late-bound engine initialization completed with status {IsRuntimeReady}.", isRuntimeReady);
                    return isRuntimeReady;
                });
            }

            if (HostEnvironmentEnvExtensions.IsDevelopment(env))
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

            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseMiddleware<PathRewriteMiddleware>();

            if (StartupConfiguration.IsUIEnabled(runtimeConfig, env))
            {
                app.UseSwaggerUI(c =>
                {
                    c.ConfigObject.Urls = new SwaggerEndpointMapper(app.ApplicationServices.GetService<RuntimeConfigProvider?>());
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
                        context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict;
                    }
                    else
                    {
                        await next.Invoke();
                    }
                }
                else
                {
                    context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable;
                }
            });

            app.UseAuthentication();
            app.UseMiddleware<Core.AuthenticationHelpers.ClientRoleHeaderAuthenticationMiddleware>();
            app.UseAuthorization();
            app.UseMiddleware<Core.Authorization.ClientRoleHeaderAuthorizationMiddleware>();

            HotChocolate.Execution.IRequestExecutorManager requestExecutorManager = app.ApplicationServices.GetRequiredService<HotChocolate.Execution.IRequestExecutorManager>();
            _hotReloadEventHandler.Subscribe(
                "GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED",
                (_, _) => StartupConfiguration.EvictGraphQLSchema(requestExecutorManager));

            app.UseEndpoints(endpoints =>
            {
                _logger.LogInformation("Mapping endpoints for controllers, GraphQL, Nitro, and health checks.");
                endpoints.MapControllers();
                endpoints.MapGraphQL().WithOptions(new HotChocolate.AspNetCore.GraphQLServerOptions
                {
                    Tool = { Enable = StartupConfiguration.IsUIEnabled(runtimeConfig, env) }
                });
                endpoints.MapNitroApp().WithOptions(new HotChocolate.AspNetCore.GraphQLToolOptions { Enable = false });
                endpoints.MapHealthChecks("/", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
                {
                    ResponseWriter = app.ApplicationServices.GetRequiredService<HealthCheck.BasicHealthReportResponseWriter>().WriteResponse
                });
            });
        }

        /// <summary>
        /// Configure telemetry in legacy mode (for tests)
        /// </summary>
        private void ConfigureTelemetryInLegacyMode(IApplicationBuilder app, RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig?.Runtime?.Telemetry is not null
                && runtimeConfig.Runtime.Telemetry.ApplicationInsights is not null)
            {
                StartupConfiguration.AppInsightsOptions = runtimeConfig.Runtime.Telemetry.ApplicationInsights;

                if (StartupConfiguration.AppInsightsOptions.Enabled && !string.IsNullOrWhiteSpace(StartupConfiguration.AppInsightsOptions.ConnectionString))
                {
                    Microsoft.ApplicationInsights.TelemetryClient? appTelemetryClient = app.ApplicationServices.GetService<Microsoft.ApplicationInsights.TelemetryClient>();
                    if (appTelemetryClient is not null)
                    {
                        Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration telemetryConfiguration = appTelemetryClient.TelemetryConfiguration;
                        telemetryConfiguration.ConnectionString = StartupConfiguration.AppInsightsOptions.ConnectionString;

                        if (StartupConfiguration.CustomTelemetryChannel is not null)
                        {
                            telemetryConfiguration.TelemetryChannel = StartupConfiguration.CustomTelemetryChannel;
                        }
                    }
                }
            }

            if (runtimeConfig?.Runtime?.Telemetry?.OpenTelemetry is not null)
            {
                StartupConfiguration.OpenTelemetryOptions = runtimeConfig.Runtime.Telemetry.OpenTelemetry;
            }

            if (runtimeConfig?.Runtime?.Telemetry?.AzureLogAnalytics is not null)
            {
                StartupConfiguration.AzureLogAnalyticsOptions = runtimeConfig.Runtime.Telemetry.AzureLogAnalytics;
            }

            if (runtimeConfig?.Runtime?.Telemetry?.File is not null)
            {
                StartupConfiguration.FileSinkOptions = runtimeConfig.Runtime.Telemetry.File;
            }
        }

        /// <summary>
        /// Perform initialization tasks when configuration changes (from StartupExtensions)
        /// </summary>
        private async Task<bool> PerformOnConfigChangeAsync(IApplicationBuilder app)
        {
            try
            {
                _logger.LogInformation("Runtime config change detected. Beginning initialization pipeline.");
                
                RuntimeConfigProvider runtimeConfigProvider = app.ApplicationServices.GetService<RuntimeConfigProvider>()!;
                RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();
                
                _logger.LogInformation("Retrieved runtime configuration. Host mode: {HostMode}.", runtimeConfig.Runtime?.Host?.Mode);

                RuntimeConfigValidator runtimeConfigValidator = app.ApplicationServices.GetService<RuntimeConfigValidator>()!;
                _logger.LogInformation("Resolved RuntimeConfigValidator. Starting core validation checks.");
                
                runtimeConfigValidator.ValidateConfigProperties();
                _logger.LogInformation("Runtime config properties validated.");

                if (runtimeConfig.IsDevelopmentMode())
                {
                    runtimeConfigValidator.ValidatePermissionsInConfig(runtimeConfig);
                    _logger.LogInformation("Validated permissions for development mode runtime.");
                }

                Core.Services.MetadataProviders.IMetadataProviderFactory sqlMetadataProviderFactory =
                    app.ApplicationServices.GetRequiredService<Core.Services.MetadataProviders.IMetadataProviderFactory>();
                _logger.LogInformation("Initializing metadata provider factory.");
                await sqlMetadataProviderFactory.InitializeAsync();
                _logger.LogInformation("Metadata provider factory initialization complete.");

                // Trigger service instantiation
                Core.Services.GraphQLSchemaCreator graphQLSchemaCreator =
                    app.ApplicationServices.GetRequiredService<Core.Services.GraphQLSchemaCreator>();
                _logger.LogInformation("GraphQL schema creator resolved.");

                Core.Services.RestService restService =
                    app.ApplicationServices.GetRequiredService<Core.Services.RestService>();
                _logger.LogInformation("REST service resolved.");

                if (runtimeConfig.IsDevelopmentMode())
                {
                    runtimeConfigValidator.ValidateRelationshipConfigCorrectness(runtimeConfig);
                    runtimeConfigValidator.ValidateRelationships(runtimeConfig, sqlMetadataProviderFactory!);
                    _logger.LogInformation("Validated relationship configuration for development mode runtime.");
                }

                // OpenAPI document generation is skipped if types are missing
                // This is expected if the Core project has compilation errors

                _logger.LogInformation("Runtime initialization completed without exceptions.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to complete runtime initialization. Refer to exception for error details.");
                return false;
            }
        }
    }
}

