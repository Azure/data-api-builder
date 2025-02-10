// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO.Abstractions;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
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
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.HealthCheck;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ZiggyCreatures.Caching.Fusion;
using CorsOptions = Azure.DataApiBuilder.Config.ObjectModel.CorsOptions;

namespace Azure.DataApiBuilder.Service
{
    public class Startup
    {
        private ILogger<Startup> _logger;

        public static LogLevel MinimumLogLevel = LogLevel.Error;

        public static bool IsLogLevelOverriddenByCli;
        public static OpenTelemetryOptions OpenTelemetryOptions = new();

        public static ApplicationInsightsOptions AppInsightsOptions = new();
        public const string NO_HTTPS_REDIRECT_FLAG = "--no-https-redirect";
        private HotReloadEventHandler<HotReloadEventArgs> _hotReloadEventHandler = new();
        private RuntimeConfigProvider? _configProvider;

        public Startup(IConfiguration configuration, ILogger<Startup> logger)
        {
            Configuration = configuration;
            _logger = logger;
        }

        public IConfiguration Configuration { get; }

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
            services.AddSingleton(_hotReloadEventHandler);
            string configFileName = Configuration.GetValue<string>("ConfigFileName") ?? FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME;
            /*string? connectionString = Configuration.GetValue<string?>(
                FileSystemRuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING.Replace(FileSystemRuntimeConfigLoader.ENVIRONMENT_PREFIX, ""),
                null);*/

            string? connectionString = "Server=x6eps4xrq2xudenlfv6naeo3i4-nckalsgxtuiu5nvuwmzl5mkmia.msit-datawarehouse.fabric.microsoft.com;Database=giolake;Persist Security Info=False;;MultipleActiveResultSets=False;Connection Timeout=5;";

            IFileSystem fileSystem = new FileSystem();
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem, _hotReloadEventHandler, configFileName, connectionString);
            RuntimeConfigProvider configProvider = new(configLoader);
            _configProvider = configProvider;

            string connectionToken = "eyJhbGciOiJSUzI1NiIsImtpZCI6IjhEODQxNEY3MTYzRDBBQ0Y5OUY0MTk0QkZDOERDRTQxRUQ2QTA0REQiLCJ4NXQiOiJqWVFVOXhZOUNzLVo5QmxMX0kzT1FlMXFCTjAiLCJ0eXAiOiJKV1QifQ.eyJpc3MiOiJtc2l0LnBiaWRlZGljYXRlZC53aW5kb3dzLm5ldCIsImV4cCI6MTczODk2MzcxNSwibmJmIjoxNzM4OTU5ODE0LCJ0b2tlblZlcnNpb24iOiIyIiwidG9rZW5UeXBlIjoiQWFkQmFzZWRUb2tlbiIsInBsYXRmb3JtQ2xhaW1zIjoie1wib3JpZ2luYWxBYWRUb2tlblwiOlwiZXlKMGVYQWlPaUpLVjFRaUxDSmhiR2NpT2lKU1V6STFOaUlzSW5nMWRDSTZJbGxVWTJWUE5VbEtlWGx4VWpacWVrUlROV2xCWW5CbE5ESktkeUlzSW10cFpDSTZJbGxVWTJWUE5VbEtlWGx4VWpacWVrUlROV2xCWW5CbE5ESktkeUo5LmV5SmhkV1FpT2lKb2RIUndjem92TDJGdVlXeDVjMmx6TG5kcGJtUnZkM011Ym1WMEwzQnZkMlZ5WW1rdllYQnBJaXdpYVhOeklqb2lhSFIwY0hNNkx5OXpkSE11ZDJsdVpHOTNjeTV1WlhRdk56Sm1PVGc0WW1ZdE9EWm1NUzAwTVdGbUxUa3hZV0l0TW1RM1kyUXdNVEZrWWpRM0x5SXNJbWxoZENJNk1UY3pPRGsxT1RneE5Dd2libUptSWpveE56TTRPVFU1T0RFMExDSmxlSEFpT2pFM016ZzVOalV3Tnpjc0ltRmpZM1FpT2pBc0ltRmpjaUk2SWpFaUxDSmhhVzhpT2lKQlUxRkNNaTgwV2tGQlFVRlZSV3BXZFM4MU9XdFdkR0Z1WmpaU2FrOVdjblJzZVU1TlZtdDZPVzlMT1U1eVdtbFNibEk1VTJSbVdIRTVaRkpNVkhRMllYRkViSGQ2WkhFelpYUlhNV2RKYWt4WFoxVk5jRkpGY2tSdWFVRXpjMUJ6YzNnMWJXRk5WMmN3UTJFcmN5OXJVVnBFV21sVFN6UjRRblV6UVZjcmVITTJTSEZrVFd4SE5uaG1RWFo0TVhWVlVuRXpla2xWWmxoTGRsQm1URlo1Y0ZKRFdVUkdlak5FV0Zad1ZIRXhVR2xZVVdZdmIxQTBhRWRZVWpVd2FXUjJaM1JJWWxrMk1ERk9VRTFxVkhjMGQyNUNjRFprWVdSSGRVSmpXRUkwUkZSb04xZFJORFZZUjAxWFIwOVNNbGg2WlROcFlYRnZSbnBRYUcxUGRXMWxRMHc1Ym1WamFrSnBjRlV2Y0VwdVVrTm1UVFJaVkZCMldESjVkRVoyTjAxUWVGQXZUamRvWTFOTFprWXZNMHM1YTFkNGFWTmtURlZSV2tvM2QyUjJTR0ZTZVZCNE0zQklhQ3RzYVVGRFFucHJTV1ZqTDFGSVIwRktPVFJXTVdVdlFtbHZPSFo1VlU1b1NFSllhRmRDTmtJeVNWUldWV016Vm1WeU5IWmtjMjR4UlZNeFNtMU1URzgyWTNRaUxDSmhiWElpT2xzaWNIZGtJaXdpY25OaElpd2liV1poSWwwc0ltRndjR2xrSWpvaU9EY3hZekF4TUdZdE5XVTJNUzAwWm1JeExUZ3pZV010T1RnMk1UQmhOMlU1TVRFd0lpd2lZWEJ3YVdSaFkzSWlPaUl3SWl3aVkyOXVkSEp2YkhNaU9sc2lZWEJ3WDNKbGN5SmRMQ0pqYjI1MGNtOXNjMTloZFdSeklqcGJJakF3TURBd01EQTVMVEF3TURBdE1EQXdNQzFqTURBd0xUQXdNREF3TURBd01EQXdNQ0lzSWpBd01EQXdNREF6TFRBd01EQXRNR1ptTVMxalpUQXdMVEF3TURBd01EQXdNREF3TUNKZExDSmtaWFpwWTJWcFpDSTZJbU5rWkdJellXSXpMV0V6WXpFdE5HVmxaaTFpTlRJMkxXWm1PRGRtWVRobU5tUmhZaUlzSW1aaGJXbHNlVjl1WVcxbElqb2lVbWxpWldseWJ5SXNJbWRwZG1WdVgyNWhiV1VpT2lKSGFXOTJZVzV1WVNJc0ltbGtkSGx3SWpvaWRYTmxjaUlzSW1sd1lXUmtjaUk2SWpJMk1ERTZOakF3T2poak9EQTZNVEl6TURwbU5EZzFPams0WW1VNlltVmtOVHBoWlRSbUlpd2libUZ0WlNJNklrZHBiM1poYm01aElGSnBZbVZwY204aUxDSnZhV1FpT2lJM00yWTNPV1poWVMweVlqRTRMVFEwTmprdE9HUTBPQzFsWkdWbE1HVTRPRFJsWlRjaUxDSnZibkJ5WlcxZmMybGtJam9pVXkweExUVXRNakV0TWpFeU56VXlNVEU0TkMweE5qQTBNREV5T1RJd0xURTRPRGM1TWpjMU1qY3ROVGs0TURNMk16UWlMQ0p3ZFdsa0lqb2lNVEF3TXpJd01ESXhPRUV6UWpkQlF5SXNJbkpvSWpvaU1TNUJVbTlCZGpScU5XTjJSMGR5TUVkU2NYa3hPREJDU0dKU2QydEJRVUZCUVVGQlFVRjNRVUZCUVVGQlFVRkJRV0ZCVHpSaFFVRXVJaXdpYzJOd0lqb2lkWE5sY2w5cGJYQmxjbk52Ym1GMGFXOXVJaXdpYzJsa0lqb2lZVEJoTUdRd05HTXRaVFUyTmkwME9UTTBMV0k1WWpZdE1UQXpORGRoWkdNNFlqTmtJaXdpYzJsbmJtbHVYM04wWVhSbElqcGJJbVIyWTE5dGJtZGtJaXdpWkhaalgyTnRjQ0lzSW10dGMya2lYU3dpYzNWaUlqb2lkSGxoWmxsa1prUkZWbGc0U0ZjM09HWTRkR1JZV2xGNVVIZGlZVmRRUmxScE5XOWFNRmd0U2pCRmJ5SXNJblJwWkNJNklqY3laams0T0dKbUxUZzJaakV0TkRGaFppMDVNV0ZpTFRKa04yTmtNREV4WkdJME55SXNJblZ1YVhGMVpWOXVZVzFsSWpvaVozSnBZbVZwY205QWJXbGpjbTl6YjJaMExtTnZiU0lzSW5Wd2JpSTZJbWR5YVdKbGFYSnZRRzFwWTNKdmMyOW1kQzVqYjIwaUxDSjFkR2tpT2lKV1RISlRjVkZXYUdZd2VVNUhNazVSZUd0b1JVRkJJaXdpZG1WeUlqb2lNUzR3SWl3aWQybGtjeUk2V3lKaU56bG1ZbVkwWkMwelpXWTVMVFEyT0RrdE9ERTBNeTAzTm1JeE9UUmxPRFUxTURraVhTd2llRzF6WDJOaklqcGJJa05RTVNKZExDSjRiWE5mYVdSeVpXd2lPaUl4TUNBeEluMC5pWDE5X3dqT3hjVE44U3lReGY1T0ZjeWFqLUZzaVM0eTAxd29XbEVBMThaTTkyTmVBak5pU0pWckZDNUU1YUI3X2hTQzk1UVBOTnNKWU5yRUxNUENsMlpSUGtqcmFUTTZCLUxLUkpoSWNaYmVOTzFpa1NaWXVyMjNwNHRHN2xBdmM2cGdVTXA5R3J6VW1selI1Rnh4SnZ2T2ZITl92UTRVWGVjWFA4R1lxZGttZHhheFRoeTA0UVRXaUJGSHFWb1VKUFNYeTh2UHRNME9OUXFMV0ItS29IaEJyaXAxakdaU2dzaXByenNfUkpnNXBOWkFydndWTHFGOFYyeTBmdXZxcDVRUjY5enlQVnRXRFptMGtyeHRPQXR0ZXlIQ0lWdlh2ZVJCYlVMd2tidlVaVXR5NjltUTlBLVRCM09WeFVKZ2phWldwdjY2LWxOYnE1cVZyR1VpZEFcIixcIm9yaWdpbmFsRmFicmljU2NvcGVzXCI6bnVsbCxcInNlcnZpY2VJZGVudGl0eVwiOm51bGwsXCJhdWRpZW5jZVwiOlwiaHR0cHM6Ly9hbmFseXNpcy53aW5kb3dzLm5ldC9wb3dlcmJpL2FwaVwiLFwicHJpbmNpcGFsT2JqZWN0SWRcIjpcIjczZjc5ZmFhLTJiMTgtNDQ2OS04ZDQ4LWVkZWUwZTg4NGVlN1wiLFwidGVuYW50T2JqZWN0SWRcIjpcIjcyZjk4OGJmLTg2ZjEtNDFhZi05MWFiLTJkN2NkMDExZGI0N1wiLFwid29ya2xvYWRJZFwiOlwiRE1TXCIsXCJ3b3Jrc3BhY2VPYmplY3RJZFwiOlwiYzgwNTk0NjgtOWRkNy00ZTExLWI2YjQtYjMzMmJlYjE0YzQwXCIsXCJjdXN0b21lckNhcGFjaXR5T2JqZWN0SWRcIjpcIjg1NEI5Q0ExLTdFOTYtNEQ4Ri05NUFELUU2NjIxRTE4Qjc5NFwiLFwid29ya3NwYWNlUGVybWlzc2lvbnNcIjpbXCJSZWFkXCIsXCJXcml0ZVwiLFwiUmVhZFdyaXRlXCIsXCJSZVNoYXJlXCIsXCJSZWFkUmVzaGFyZVwiLFwiQWxsXCIsXCJFeHBsb3JlXCIsXCJSZWFkRXhwbG9yZVwiLFwiUmVhZFdyaXRlRXhwbG9yZVwiLFwiUmVhZFJlc2hhcmVFeHBsb3JlXCIsXCJSZWFkV3JpdGVSZXNoYXJlRXhwbG9yZVwiXSxcImFydGlmYWN0c1wiOltdLFwiYXJ0aWZhY3RzRXhwYW5zaW9uUmVxdWlyZWRcIjpmYWxzZSxcImZhYnJpY0ludHJhU2VydmljZUNhbGxcIjpmYWxzZSxcInNraXBUaHJvdHRsaW5nXCI6ZmFsc2UsXCJzb3VyY2VDYXBhY2l0eU9iamVjdElkXCI6bnVsbCxcImFjXCI6bnVsbCxcImFpZFwiOm51bGwsXCJhdHlwXCI6bnVsbCxcInByaXZpbGVnZWRPbmVMYWtlRGF0YUFjY2Vzc1wiOm51bGx9Iiwid29ya2xvYWRDbGFpbXMiOiJ7XHJcbiAgXCJ3b3Jrc3BhY2VJZFwiOiBcImM4MDU5NDY4LTlkZDctNGUxMS1iNmI0LWIzMzJiZWIxNGM0MFwiLFxyXG4gIFwiaWRlbnRpdHlcIjogbnVsbCxcclxuICBcInJvbGVzXCI6IG51bGxcclxufSIsImlhdCI6MTczODk2MDExNX0.CMJWxnE02rOMyZTe_83IkLFW2YqcDI5Rep2UIH6EbOfkMzcZ7VoxmWk5jE4rIJ8RQHxYZ0tHPkP2Sjh1XK3k3-5L6ImaT83a-4esfGfWEau6KZB38xXxNxKEr5s2d8-A7MuXSDQo3ceggaqfzTRdbQ3lKqDkbY5qnKyn91AmN2EsJHcuo3eiKWaMVi0E7PjErSsBkh-rKmTpna-dmyf2DHzTxWJg87_FHJL78EsGuR0CPzLjWmG6v5bGx5brm8olQ1V3ikzOd15GcKtgllnCSk43mgLmb83cP4yLI-3HdpDyXqxdEwoqlMyfDeK7ofBqYtVPZNsPx0KWuEOGirUHjQ";

            _configProvider.TryGetConfig(out RuntimeConfig? runtimeConfigTest);

            // the connection token could be null for basic auth type.
            _configProvider.TrySetAccesstoken(connectionToken, runtimeConfigTest!.DefaultDataSourceName); // P

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
                services.AddOpenTelemetry()
                .WithMetrics(metrics =>
                {
                    metrics.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(runtimeConfig.Runtime.Telemetry.OpenTelemetry.ServiceName!))
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddOtlpExporter(configure =>
                        {
                            configure.Endpoint = new Uri(runtimeConfig.Runtime.Telemetry.OpenTelemetry.Endpoint!);
                            configure.Headers = runtimeConfig.Runtime.Telemetry.OpenTelemetry.Headers;
                            configure.Protocol = OtlpExportProtocol.Grpc;
                        })
                        .AddRuntimeInstrumentation();
                })
                .WithTracing(tracing =>
                {
                    tracing.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(runtimeConfig.Runtime.Telemetry.OpenTelemetry.ServiceName!))
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddOtlpExporter(configure =>
                        {
                            configure.Endpoint = new Uri(runtimeConfig.Runtime.Telemetry.OpenTelemetry.Endpoint!);
                            configure.Headers = runtimeConfig.Runtime.Telemetry.OpenTelemetry.Headers;
                            configure.Protocol = OtlpExportProtocol.Grpc;
                        });
                });
            }

            services.AddSingleton(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<RuntimeConfigValidator>();
            });
            services.AddSingleton<RuntimeConfigValidator>();

            services.AddSingleton<CosmosClientProvider>();
            services.AddHealthChecks()
                .AddCheck<DabHealthCheck>("DabHealthCheck");

            services.AddSingleton<ILogger<SqlQueryEngine>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<SqlQueryEngine>();
            });

            services.AddSingleton<ILogger<IQueryExecutor>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<IQueryExecutor>();
            });

            services.AddSingleton<ILogger<ISqlMetadataProvider>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
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
            services.AddSingleton<HealthReportResponseWriter>();

            // ILogger explicit creation required for logger to use --LogLevel startup argument specified.
            services.AddSingleton<ILogger<HealthReportResponseWriter>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<HealthReportResponseWriter>();
            });

            services.AddSingleton<ILogger<RestController>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<RestController>();
            });

            services.AddSingleton<ILogger<ClientRoleHeaderAuthenticationMiddleware>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<ClientRoleHeaderAuthenticationMiddleware>();
            });

            services.AddSingleton<ILogger<ConfigurationController>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<ConfigurationController>();
            });

            //Enable accessing HttpContext in RestService to get ClaimsPrincipal.
            services.AddHttpContextAccessor();

            if (runtimeConfig is not null && runtimeConfig.Runtime?.Host?.Mode is HostMode.Development)
            {
                // Development mode implies support for "Hot Reload". The V2 authentication function
                // wires up all DAB supported authentication providers (schemes) so that at request time,
                // the runtime config defined authenitication provider is used to authenticate requests.
                ConfigureAuthenticationV2(services, configProvider);
            }
            else
            {
                ConfigureAuthentication(services, configProvider);
            }

            services.AddAuthorization();
            services.AddSingleton<ILogger<IAuthorizationHandler>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<IAuthorizationHandler>();
            });
            services.AddSingleton<ILogger<IAuthorizationResolver>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<IAuthorizationResolver>();
            });

            services.AddSingleton<IAuthorizationHandler, RestAuthorizationHandler>();
            services.AddSingleton<IAuthorizationResolver, AuthorizationResolver>();
            services.AddSingleton<IOpenApiDocumentor, OpenApiDocumentor>();

            AddGraphQLService(services, runtimeConfig?.Runtime?.GraphQL);

            // Subscribe the GraphQL schema refresh method to the specific hot-reload event
            _hotReloadEventHandler.Subscribe(DabConfigEvents.GRAPHQL_SCHEMA_REFRESH_ON_CONFIG_CHANGED, (sender, args) => RefreshGraphQLSchema(services));

            services.AddFusionCache()
                .WithOptions(options =>
                {
                    options.FactoryErrorsLogLevel = LogLevel.Debug;
                    options.EventHandlingErrorsLogLevel = LogLevel.Debug;
                })
                .WithDefaultEntryOptions(new FusionCacheEntryOptions
                {
                    Duration = TimeSpan.FromSeconds(5)
                });

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
        private void AddGraphQLService(IServiceCollection services, GraphQLRuntimeOptions? graphQLRuntimeOptions)
        {
            IRequestExecutorBuilder server = services.AddGraphQLServer()
                .AddHttpRequestInterceptor<DefaultHttpRequestInterceptor>()
                .ConfigureSchema((serviceProvider, schemaBuilder) =>
                {
                    GraphQLSchemaCreator graphQLService = serviceProvider.GetRequiredService<GraphQLSchemaCreator>();
                    graphQLService.InitializeSchemaAndResolvers(schemaBuilder);
                })
                .AddHttpRequestInterceptor<IntrospectionInterceptor>()
                .AddAuthorization()
                .AllowIntrospection(false)
                .AddAuthorizationHandler<GraphQLAuthorizationHandler>();

            // Conditionally adds a maximum depth rule to the GraphQL queries/mutation selection set.
            // This rule is only added if a positive depth limit is specified, ensuring that the server
            // enforces a limit on the depth of incoming GraphQL queries/mutation to prevent extremely deep queries
            // that could potentially lead to performance issues.
            // Additionally, the skipIntrospectionFields parameter is set to true to skip depth limit enforcement on introspection queries.
            if (graphQLRuntimeOptions is not null && graphQLRuntimeOptions.DepthLimit.HasValue && graphQLRuntimeOptions.DepthLimit.Value > 0)
            {
                server = server.AddMaxExecutionDepthRule(maxAllowedExecutionDepth: graphQLRuntimeOptions.DepthLimit.Value, skipIntrospectionFields: true);
            }

            // Allows DAB to override the HTTP error code set by HotChocolate.
            // This is used to ensure HTTP code 4XX is set when the datatbase
            // returns a "bad request" error such as stored procedure params missing.
            services.AddHttpResultSerializer<DabGraphQLResultSerializer>();

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
                        return error.RemoveException()
                                .RemoveLocations()
                                .RemovePath()
                                .WithMessage(thrownException.Message)
                                .WithCode($"{thrownException.SubStatusCode}");
                    }

                    return error;
                })
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
            AddGraphQLService(services, runtimeConfig?.Runtime?.GraphQL);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, RuntimeConfigProvider runtimeConfigProvider, IHostApplicationLifetime hostLifetime)
        {
            bool isRuntimeReady = false;

            if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                // Configure Application Insights Telemetry
                ConfigureApplicationInsightsTelemetry(app, runtimeConfig);

                // Config provided before starting the engine.
                isRuntimeReady = PerformOnConfigChangeAsync(app).Result;

                if (!isRuntimeReady)
                {
                    // Exiting if config provided is Invalid.
                    if (_logger is not null)
                    {
                        _logger.LogError(
                            message: "Could not initialize the engine with the runtime config file: {configFilePath}",
                            runtimeConfigProvider.ConfigFilePath);
                    }

                    hostLifetime.StopApplication();
                }
            }
            else
            {
                // Config provided during runtime.
                runtimeConfigProvider.IsLateConfigured = true;
                runtimeConfigProvider.RuntimeConfigLoadedHandlers.Add(async (sender, newConfig) =>
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
                app.UseHttpsRedirection();
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
                app.UseSwaggerUI(c =>
                {
                    c.ConfigObject.Urls = new SwaggerEndpointMapper(app.ApplicationServices.GetService<RuntimeConfigProvider?>());
                });
            }

            app.UseRouting();

            // Adding CORS Middleware
            if (runtimeConfig is not null && runtimeConfig.Runtime?.Host?.Cors is not null)
            {
                app.UseCors(CORSPolicyBuilder =>
                {
                    CorsOptions corsConfig = runtimeConfig.Runtime.Host.Cors;
                    ConfigureCors(CORSPolicyBuilder, corsConfig);
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
            _hotReloadEventHandler.Subscribe("GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED", (sender, args) => EvictGraphQLSchema(requestExecutorResolver));

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();

                endpoints.MapGraphQL(GraphQLRuntimeOptions.DEFAULT_PATH).WithOptions(new GraphQLServerOptions
                {
                    Tool = {
                        // Determines if accessing the endpoint from a browser
                        // will load the GraphQL Banana Cake Pop IDE.
                        Enable = IsUIEnabled(runtimeConfig, env)
                    }
                });

                // In development mode, BCP is enabled at /graphql endpoint by default.
                // Need to disable mapping BCP explicitly as well to avoid ability to query
                // at an additional endpoint: /graphql/ui.
                endpoints.MapBananaCakePop().WithOptions(new GraphQLToolOptions
                {
                    Enable = false
                });

                endpoints.MapHealthChecks("/", new HealthCheckOptions
                {
                    ResponseWriter = app.ApplicationServices.GetRequiredService<HealthReportResponseWriter>().WriteResponse
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
        public static ILoggerFactory CreateLoggerFactoryForHostedAndNonHostedScenario(IServiceProvider serviceProvider)
        {
            if (!IsLogLevelOverriddenByCli)
            {
                // If the log level is not overridden by command line arguments specified through CLI,
                // attempt to get the runtime config to determine the loglevel based on host.mode.
                // If runtime config is available, set the loglevel to Error if host.mode is Production,
                // Debug if it is Development.
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                if (configProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
                {
                    MinimumLogLevel = RuntimeConfig.GetConfiguredLogLevel(runtimeConfig);
                }
            }

            TelemetryClient? appTelemetryClient = serviceProvider.GetService<TelemetryClient>();

            return Program.GetLoggerFactoryForLogLevel(MinimumLogLevel, appTelemetryClient);
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
        /// - IOptionsChangeTokenSource<JwtBearerOptions> : Registers a change token source for dynamic config updates which
        /// is used internally by JwtBearerHandler's OptionsMonitor to listen for changes in JwtBearerOptions.
        /// - IConfigureOptions<JwtBearerOptions> : Registers named JwtBearerOptions whose "Configure(...)" function is
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
        /// <param name="runtimeConfigurationProvider">The provider used to load runtime configuration.</param>
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
                ILoggerFactory? loggerFactory = Program.GetLoggerFactoryForLogLevel(MinimumLogLevel, appTelemetryClient);
                _logger = loggerFactory.CreateLogger<Startup>();
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

                if (sqlMetadataProviderFactory is not null)
                {
                    await sqlMetadataProviderFactory.InitializeAsync();
                }

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
            string[] Origins = corsConfig.Origins is not null ? corsConfig.Origins : Array.Empty<string>();
            if (corsConfig.AllowCredentials)
            {
                return builder
                    .WithOrigins(Origins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowCredentials()
                    .Build();
            }
            else
            {
                return builder
                    .WithOrigins(Origins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .Build();
            }
        }

        /// <summary>
        /// Indicates whether to provide UI visualization of REST(via Swagger) or GraphQL (via Banana CakePop).
        /// </summary>
        private static bool IsUIEnabled(RuntimeConfig? runtimeConfig, IWebHostEnvironment env)
        {
            return (runtimeConfig is not null && runtimeConfig.IsDevelopmentMode()) || env.IsDevelopment();
        }
    }
}
