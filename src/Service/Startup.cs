// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IdentityModel.Tokens.Jwt;
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
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Services.OpenAPI;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.AspNetCore;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CorsOptions = Azure.DataApiBuilder.Config.ObjectModel.CorsOptions;

namespace Azure.DataApiBuilder.Service
{
    public class Startup
    {
        private ILogger<Startup> _logger;

        public static LogLevel MinimumLogLevel = LogLevel.Error;

        public static bool IsLogLevelOverriddenByCli;

        public const string NO_HTTPS_REDIRECT_FLAG = "--no-https-redirect";

        public Startup(IConfiguration configuration, ILogger<Startup> logger)
        {
            Configuration = configuration;
            _logger = logger;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            string configFileName = Configuration.GetValue<string>("ConfigFileName", FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME);
            string? connectionString = Configuration.GetValue<string?>(
                FileSystemRuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING.Replace(FileSystemRuntimeConfigLoader.ENVIRONMENT_PREFIX, ""),
                null);
            IFileSystem fileSystem = new FileSystem();
            FileSystemRuntimeConfigLoader configLoader = new(fileSystem, configFileName, connectionString);
            RuntimeConfigProvider configProvider = new(configLoader);

            services.AddSingleton(fileSystem);
            services.AddSingleton(configProvider);
            services.AddSingleton(configLoader);
            services.AddSingleton(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<RuntimeConfigValidator>();
            });
            services.AddSingleton<RuntimeConfigValidator>();

            services.AddSingleton<CosmosClientProvider>();
            services.AddHealthChecks();

            services.AddSingleton<ILogger<SqlQueryEngine>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<SqlQueryEngine>();
            });

            services.AddSingleton<IQueryEngine>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetConfig();

                return runtimeConfig.DataSource.DatabaseType switch
                {
                    DatabaseType.CosmosDB_NoSQL => ActivatorUtilities.GetServiceOrCreateInstance<CosmosQueryEngine>(serviceProvider),
                    DatabaseType.MSSQL or DatabaseType.PostgreSQL or DatabaseType.MySQL => ActivatorUtilities.GetServiceOrCreateInstance<SqlQueryEngine>(serviceProvider),
                    _ => throw new NotSupportedException(runtimeConfig.DataSource.DatabaseTypeNotSupportedMessage),
                };
            });

            services.AddSingleton<IMutationEngine>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetConfig();

                return runtimeConfig.DataSource.DatabaseType switch
                {
                    DatabaseType.CosmosDB_NoSQL => ActivatorUtilities.GetServiceOrCreateInstance<CosmosMutationEngine>(serviceProvider),
                    DatabaseType.MSSQL or DatabaseType.PostgreSQL or DatabaseType.MySQL => ActivatorUtilities.GetServiceOrCreateInstance<SqlMutationEngine>(serviceProvider),
                    _ => throw new NotSupportedException(runtimeConfig.DataSource.DatabaseTypeNotSupportedMessage),
                };
            });

            services.AddSingleton<ILogger<IQueryExecutor>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<IQueryExecutor>();
            });
            services.AddSingleton<IQueryExecutor>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetConfig();

                return runtimeConfig.DataSource.DatabaseType switch
                {
                    DatabaseType.CosmosDB_NoSQL => null!,
                    DatabaseType.MSSQL => ActivatorUtilities.GetServiceOrCreateInstance<MsSqlQueryExecutor>(serviceProvider),
                    DatabaseType.PostgreSQL => ActivatorUtilities.GetServiceOrCreateInstance<PostgreSqlQueryExecutor>(serviceProvider),
                    DatabaseType.MySQL => ActivatorUtilities.GetServiceOrCreateInstance<MySqlQueryExecutor>(serviceProvider),
                    _ => throw new NotSupportedException(runtimeConfig.DataSource.DatabaseTypeNotSupportedMessage),
                };
            });

            services.AddSingleton<IQueryBuilder>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetConfig();

                return runtimeConfig.DataSource.DatabaseType switch
                {
                    DatabaseType.CosmosDB_NoSQL => null!,
                    DatabaseType.MSSQL => ActivatorUtilities.GetServiceOrCreateInstance<MsSqlQueryBuilder>(serviceProvider),
                    DatabaseType.PostgreSQL => ActivatorUtilities.GetServiceOrCreateInstance<PostgresQueryBuilder>(serviceProvider),
                    DatabaseType.MySQL => ActivatorUtilities.GetServiceOrCreateInstance<MySqlQueryBuilder>(serviceProvider),
                    _ => throw new NotSupportedException(runtimeConfig.DataSource.DatabaseTypeNotSupportedMessage),
                };
            });

            services.AddSingleton<ILogger<ISqlMetadataProvider>>(implementationFactory: (serviceProvider) =>
            {
                ILoggerFactory? loggerFactory = CreateLoggerFactoryForHostedAndNonHostedScenario(serviceProvider);
                return loggerFactory.CreateLogger<ISqlMetadataProvider>();
            });

            services.AddSingleton<ISqlMetadataProvider>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetConfig();

                return runtimeConfig.DataSource.DatabaseType switch
                {
                    DatabaseType.CosmosDB_NoSQL => ActivatorUtilities.GetServiceOrCreateInstance<CosmosSqlMetadataProvider>(serviceProvider),
                    DatabaseType.MSSQL => ActivatorUtilities.GetServiceOrCreateInstance<MsSqlMetadataProvider>(serviceProvider),
                    DatabaseType.PostgreSQL => ActivatorUtilities.GetServiceOrCreateInstance<PostgreSqlMetadataProvider>(serviceProvider),
                    DatabaseType.MySQL => ActivatorUtilities.GetServiceOrCreateInstance<MySqlMetadataProvider>(serviceProvider),
                    _ => throw new NotSupportedException(runtimeConfig.DataSource.DatabaseTypeNotSupportedMessage),
                };
            });

            services.AddSingleton<DbExceptionParser>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetConfig();

                return runtimeConfig.DataSource.DatabaseType switch
                {
                    DatabaseType.CosmosDB_NoSQL => null!,
                    DatabaseType.MSSQL => ActivatorUtilities.GetServiceOrCreateInstance<MsSqlDbExceptionParser>(serviceProvider),
                    DatabaseType.PostgreSQL => ActivatorUtilities.GetServiceOrCreateInstance<PostgreSqlDbExceptionParser>(serviceProvider),
                    DatabaseType.MySQL => ActivatorUtilities.GetServiceOrCreateInstance<MySqlDbExceptionParser>(serviceProvider),
                    _ => throw new NotSupportedException(runtimeConfig.DataSource.DatabaseTypeNotSupportedMessage),
                };
            });

            services.AddSingleton<GraphQLSchemaCreator>();
            services.AddSingleton<GQLFilterParser>();
            services.AddSingleton<RestService>();

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

            ConfigureAuthentication(services, configProvider);

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

            AddGraphQLService(services);

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
        private void AddGraphQLService(IServiceCollection services)
        {
            services.AddGraphQLServer()
                    .AddHttpRequestInterceptor<DefaultHttpRequestInterceptor>()
                    .ConfigureSchema((serviceProvider, schemaBuilder) =>
                    {
                        GraphQLSchemaCreator graphQLService = serviceProvider.GetRequiredService<GraphQLSchemaCreator>();
                        graphQLService.InitializeSchemaAndResolvers(schemaBuilder);
                    })
                    .AddHttpRequestInterceptor<IntrospectionInterceptor>()
                    .AddAuthorization()
                    .AllowIntrospection(false)
                    .AddAuthorizationHandler<GraphQLAuthorizationHandler>()
                    .AddErrorFilter(error =>
                    {
                        if (error.Exception is not null)
                        {
                            _logger.LogError(error.Exception.Message);
                            _logger.LogError(error.Exception.StackTrace);
                            return error.WithMessage(error.Exception.Message);
                        }

                        if (error.Code is not null)
                        {
                            _logger.LogError(error.Code);
                            _logger.LogError(error.Message);
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
                    });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, RuntimeConfigProvider runtimeConfigProvider)
        {
            bool isRuntimeReady = false;
            FileSystemRuntimeConfigLoader fileSystemRuntimeConfigLoader = (FileSystemRuntimeConfigLoader)runtimeConfigProvider.ConfigLoader;

            if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                // Config provided before starting the engine.
                isRuntimeReady = PerformOnConfigChangeAsync(app).Result;
                if (_logger is not null)
                {
                    _logger.LogDebug("Loaded config file from {filePath}", fileSystemRuntimeConfigLoader.ConfigFileName);
                }

                if (!isRuntimeReady)
                {
                    // Exiting if config provided is Invalid.
                    if (_logger is not null)
                    {
                        _logger.LogError("Exiting the runtime engine...");
                    }

                    throw new ApplicationException($"Could not initialize the engine with the runtime config file: {fileSystemRuntimeConfigLoader.ConfigFileName}");
                }
            }
            else
            {
                // Config provided during runtime.
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
            if (runtimeConfig?.Runtime.Host.Mode is HostMode.Development || env.IsDevelopment())
            {
                app.UseSwaggerUI(c =>
                {
                    c.ConfigObject.Urls = new SwaggerEndpointMapper(app.ApplicationServices.GetService<RuntimeConfigProvider?>());
                });
            }

            app.UseRouting();

            // Adding CORS Middleware
            if (runtimeConfig is not null && runtimeConfig.Runtime.Host.Cors is not null)
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

            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

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

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();

                endpoints.MapGraphQL(GraphQLRuntimeOptions.DEFAULT_PATH).WithOptions(new GraphQLServerOptions
                {
                    Tool = {
                        // Determines if accessing the endpoint from a browser
                        // will load the GraphQL Banana Cake Pop IDE.
                        Enable = runtimeConfig?.Runtime.Host.Mode == HostMode.Development || env.IsDevelopment()
                    }
                });

                // In development mode, BCP is enabled at /graphql endpoint by default.
                // Need to disable mapping BCP explicitly as well to avoid ability to query
                // at an additional endpoint: /graphql/ui.
                endpoints.MapBananaCakePop().WithOptions(new GraphQLToolOptions
                {
                    Enable = false
                });

                endpoints.MapHealthChecks("/");
            });
        }

        /// <summary>
        /// Takes in the RuntimeConfig object and checks the host mode.
        /// If host mode is Development, return `LogLevel.Debug`, else
        /// for production returns `LogLevel.Error`.
        /// </summary>
        public static LogLevel GetLogLevelBasedOnMode(RuntimeConfig runtimeConfig)
        {
            if (runtimeConfig.Runtime.Host.Mode == HostMode.Development)
            {
                return LogLevel.Debug;
            }

            return LogLevel.Error;
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
                    MinimumLogLevel = GetLogLevelBasedOnMode(runtimeConfig);
                }
            }

            return Program.GetLoggerFactoryForLogLevel(MinimumLogLevel);
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
            if (runtimeConfigurationProvider.TryGetConfig(out RuntimeConfig? runtimeConfig) && runtimeConfig.Runtime.Host.Authentication is not null)
            {
                AuthenticationOptions authOptions = runtimeConfig.Runtime.Host.Authentication;
                HostMode mode = runtimeConfig.Runtime.Host.Mode;
                if (!authOptions.IsAuthenticationSimulatorEnabled() && !authOptions.IsEasyAuthAuthenticationProvider())
                {
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.Audience = authOptions.Jwt!.Audience;
                        options.Authority = authOptions.Jwt!.Issuer;
                        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
                        {
                            // Instructs the asp.net core middleware to use the data in the "roles" claim for User.IsInRole()
                            // See https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal.isinrole?view=net-6.0#remarks
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

                runtimeConfigValidator.ValidateConfig();

                if (runtimeConfig.Runtime.Host.Mode == HostMode.Development)
                {
                    // Running only in developer mode to ensure fast and smooth startup in production.
                    RuntimeConfigValidator.ValidatePermissionsInConfig(runtimeConfig);
                }

                ISqlMetadataProvider sqlMetadataProvider =
                    app.ApplicationServices.GetRequiredService<ISqlMetadataProvider>();

                if (sqlMetadataProvider is not null)
                {
                    await sqlMetadataProvider.InitializeAsync();
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
                    _logger.LogError($"Endpoint service initialization failed");
                }

                if (runtimeConfig.Runtime.Host.Mode == HostMode.Development)
                {
                    // Running only in developer mode to ensure fast and smooth startup in production.
                    runtimeConfigValidator.ValidateRelationshipsInConfig(runtimeConfig, sqlMetadataProvider!);
                }

                runtimeConfigValidator.ValidateStoredProceduresInConfig(runtimeConfig, sqlMetadataProvider!);

                // Attempt to create OpenAPI document.
                // Errors must not crash nor halt the intialization of the engine
                // because OpenAPI document creation is not required for the engine to operate.
                // Errors will be logged.
                try
                {
                    IOpenApiDocumentor openApiDocumentor = app.ApplicationServices.GetRequiredService<IOpenApiDocumentor>();
                    openApiDocumentor.CreateDocument();
                }
                catch (DataApiBuilderException dabException)
                {
                    _logger.LogError($"{dabException.Message}");
                }

                _logger.LogInformation($"Successfully completed runtime initialization.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unable to complete runtime initialization. Refer to exception for error details.");
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
    }
}
