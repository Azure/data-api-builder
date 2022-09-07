using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO.Abstractions;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.AuthenticationHelpers;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services;
using Azure.DataApiBuilder.Service.Services.MetadataProviders;
using HotChocolate.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;

namespace Azure.DataApiBuilder.Service
{
    public class Startup
    {
        private ILogger<Startup> _logger;
        private ILogger<RuntimeConfigProvider> _configProviderLogger;

        public Startup(IConfiguration configuration,
            ILogger<Startup> logger,
            ILogger<RuntimeConfigProvider> configProviderLogger)
        {
            Configuration = configuration;
            _logger = logger;
            _configProviderLogger = configProviderLogger;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            RuntimeConfigPath runtimeConfigPath = new();
            Configuration.Bind(runtimeConfigPath);

            RuntimeConfigProvider runtimeConfigurationProvider = new(runtimeConfigPath, _configProviderLogger);

            services.AddSingleton(runtimeConfigurationProvider);
            services.AddSingleton<RuntimeConfigValidator>();

            services.AddSingleton<CosmosClientProvider>();
            services.AddHealthChecks();

            services.AddSingleton<IQueryEngine>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetRuntimeConfiguration();

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosQueryEngine>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlQueryEngine>(serviceProvider);
                    default:
                        throw new NotSupportedException(runtimeConfig.DatabaseTypeNotSupportedMessage);
                }
            });

            services.AddSingleton<IMutationEngine>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetRuntimeConfiguration();

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosMutationEngine>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlMutationEngine>(serviceProvider);
                    default:
                        throw new NotSupportedException(runtimeConfig.DatabaseTypeNotSupportedMessage);
                }
            });

            services.AddSingleton<IQueryExecutor>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetRuntimeConfiguration();

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return null!;
                    case DatabaseType.mssql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<QueryExecutor<SqlConnection>>(serviceProvider);
                    case DatabaseType.postgresql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<QueryExecutor<NpgsqlConnection>>(serviceProvider);
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<QueryExecutor<MySqlConnection>>(serviceProvider);
                    default:
                        throw new NotSupportedException(
                            runtimeConfig.DatabaseTypeNotSupportedMessage);
                }
            });

            services.AddSingleton<IQueryBuilder>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetRuntimeConfiguration();

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return null!;
                    case DatabaseType.mssql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<MsSqlQueryBuilder>(serviceProvider);
                    case DatabaseType.postgresql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<PostgresQueryBuilder>(serviceProvider);
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<MySqlQueryBuilder>(serviceProvider);
                    default:
                        throw new NotSupportedException(runtimeConfig.DatabaseTypeNotSupportedMessage);
                }
            });

            services.AddSingleton<ISqlMetadataProvider>(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetRuntimeConfiguration();

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosSqlMetadataProvider>(serviceProvider);
                    case DatabaseType.mssql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<MsSqlMetadataProvider>(serviceProvider);
                    case DatabaseType.postgresql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<PostgreSqlMetadataProvider>(serviceProvider);
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<MySqlMetadataProvider>(serviceProvider);
                    default:
                        throw new NotSupportedException(runtimeConfig.DatabaseTypeNotSupportedMessage);
                }
            });

            services.AddSingleton(implementationFactory: (serviceProvider) =>
            {
                RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
                RuntimeConfig runtimeConfig = configProvider.GetRuntimeConfiguration();

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return null!;
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return new DbExceptionParser(configProvider);
                    default:
                        throw new NotSupportedException(runtimeConfig.DatabaseTypeNotSupportedMessage);
                }
            });

            services.AddSingleton<GraphQLSchemaCreator>();
            services.AddSingleton<RestService>();
            services.AddSingleton<IFileSystem, FileSystem>();

            //Enable accessing HttpContext in RestService to get ClaimsPrincipal.
            services.AddHttpContextAccessor();

            ConfigureAuthentication(services, runtimeConfigurationProvider);

            services.AddAuthorization();
            services.AddSingleton<IAuthorizationHandler, RestAuthorizationHandler>();
            services.AddSingleton<IAuthorizationResolver, AuthorizationResolver>();

            AddGraphQL(services);

            services.AddControllers();
        }

        private void AddGraphQL(IServiceCollection services)
        {
            services.AddGraphQLServer()
                    .AddHttpRequestInterceptor<DefaultHttpRequestInterceptor>()
                    .ConfigureSchema((serviceProvider, schemaBuilder) =>
                    {
                        GraphQLSchemaCreator graphQLService = serviceProvider.GetRequiredService<GraphQLSchemaCreator>();
                        graphQLService.InitializeSchemaAndResolvers(schemaBuilder);
                    })
                    .AddAuthorization()
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
            if (runtimeConfigProvider.TryGetRuntimeConfiguration(out RuntimeConfig? runtimeConfig))
            {
                isRuntimeReady = PerformOnConfigChangeAsync(app).Result;
            }
            else
            {
                runtimeConfigProvider.RuntimeConfigLoaded += async (sender, newConfig) =>
                {
                    isRuntimeReady = await PerformOnConfigChangeAsync(app);
                };
            }

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            // Adding CORS Middleware
            if (runtimeConfig is not null && runtimeConfig.HostGlobalSettings.Cors is not null)
            {
                app.UseCors(CORSPolicyBuilder =>
                {
                    Cors corsConfig = runtimeConfig.HostGlobalSettings.Cors;
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

            // Add authentication middleware to the pipeline.
            if (runtimeConfig is not null)
            {
                app.UseAuthenticationMiddleware();
            }

            app.UseAuthorization();

            // Authorization Engine middleware enforces that all requests (including introspection)
            // include proper auth headers.
            // - {Authorization header + Client role header for JWT}
            // - {X-MS-CLIENT-PRINCIPAL + Client role header for EasyAuth}
            // When enabled, the middleware will prevent Banana Cake Pop(GraphQL client) from loading
            // without proper authorization headers.
            if (runtimeConfig is not null && runtimeConfig.HostGlobalSettings.Mode == HostModeType.Production)
            {
                app.UseAuthorizationEngineMiddleware();
            }

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                if (runtimeConfig is not null && runtimeConfig.GraphQLGlobalSettings.Enabled)
                {
                    endpoints.MapGraphQL(runtimeConfig.GraphQLGlobalSettings.Path);
                    endpoints.MapBananaCakePop();
                }

                endpoints.MapHealthChecks("/");
            });
        }

        /// <summary>
        /// Add services necessary for Authentication Middleware and based on the loaded
        /// runtime configuration set the AuthenticationOptions to be either
        /// EasyAuth based (by default) or JwtBearerOptions.
        /// </summary>
        /// <param name="services">The service collection to add authentication services to.</param>
        /// <param name="runtimeConfig">The loaded runtime configuration.</param>
        private static void ConfigureAuthentication(IServiceCollection services, RuntimeConfigProvider runtimeConfigurationProvider)
        {
            if (runtimeConfigurationProvider.TryGetRuntimeConfiguration(out RuntimeConfig? runtimeConfig))
            {
                // Parameterless AddAuthentication() , i.e. No defaultScheme, allows the custom JWT middleware
                // to manually call JwtBearerHandler.HandleAuthenticateAsync() and populate the User if successful.
                // This also enables the custom middleware to send the AuthN failure reason in the challenge header.
                if (runtimeConfig!.AuthNConfig != null &&
                    !runtimeConfig.IsEasyAuthAuthenticationProvider())
                {
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.Audience = runtimeConfig.AuthNConfig.Jwt!.Audience;
                        options.Authority = runtimeConfig.AuthNConfig.Jwt!.Issuer;
                    });
                }
                else if (runtimeConfig!.AuthNConfig != null)
                {
                    services.AddAuthentication(EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME)
                        .AddEasyAuthAuthentication(
                            (EasyAuthType)Enum.Parse(typeof(EasyAuthType),
                                runtimeConfig.AuthNConfig.Provider,
                                ignoreCase: true));
                }
                else
                // If no authentication configuration section specified, defaults to StaticWebApps EasyAuth.
                {
                    services.AddAuthentication(EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME)
                        .AddEasyAuthAuthentication(EasyAuthType.StaticWebApps);
                }
            }
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
                RuntimeConfig runtimeConfig = app.ApplicationServices.GetService<RuntimeConfigProvider>()!.GetRuntimeConfiguration();
                RuntimeConfigValidator runtimeConfigValidator = app.ApplicationServices.GetService<RuntimeConfigValidator>()!;

                // Now that the configuration has been set, perform validation of the runtime config
                // itself.
                runtimeConfigValidator.ValidateConfig();

                if (app.ApplicationServices.GetService<RuntimeConfigProvider>()!.IsDeveloperMode())
                {
                    // Running only in developer mode to ensure fast and smooth startup in production.
                    runtimeConfigValidator.ValidatePermissionsInConfig(runtimeConfig);
                }

                // Pre-process the permissions section in the runtimeconfig.
                runtimeConfigValidator.ProcessPermissionsInConfig(runtimeConfig);

                ISqlMetadataProvider sqlMetadataProvider =
                    app.ApplicationServices.GetRequiredService<ISqlMetadataProvider>();

                if (sqlMetadataProvider is not null)
                {
                    await sqlMetadataProvider.InitializeAsync();
                }

                if (app.ApplicationServices.GetService<RuntimeConfigProvider>()!.IsDeveloperMode())
                {
                    // Running only in developer mode to ensure fast and smooth startup in production.
                    runtimeConfigValidator.ValidateRelationshipsInConfig(runtimeConfig, sqlMetadataProvider!);
                }

                _logger.LogInformation($"Successfully completed runtime initialization.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unable to complete runtime " +
                    $"intialization operations due to: \n{ex}");
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
        public static CorsPolicy ConfigureCors(CorsPolicyBuilder builder, Cors corsConfig)
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
