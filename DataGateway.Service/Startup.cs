using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.AuthenticationHelpers;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using MySqlConnector;
using Npgsql;

namespace Azure.DataGateway.Service
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
        private IChangeToken? _inMemoryConfigChangeToken;

        private void OnConfigurationChanged(object state)
        {
            RuntimeConfigPath runtimeConfigPath = new();
            Configuration.Bind(runtimeConfigPath);
            runtimeConfigPath.SetRuntimeConfigValue();
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<RuntimeConfigPath>(Configuration);

            if (Configuration is IConfigurationRoot root)
            {
                if (root.Providers.First(prov => prov is InMemoryUpdateableConfigurationProvider) is InMemoryUpdateableConfigurationProvider provider)
                {
                    services.AddSingleton(provider);
                    _inMemoryConfigChangeToken = provider.GetReloadToken();
                    _inMemoryConfigChangeToken.RegisterChangeCallback(new Action<object>(OnConfigurationChanged), provider);
                }
            }

            services.AddSingleton<RuntimeConfigValidator>();
            services.AddSingleton<IGraphQLMetadataProvider>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfigPath>>(serviceProvider);
                RuntimeConfig runtimeConfig = runtimeConfigPath.CurrentValue.ConfigValue!;

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<GraphQLFileMetadataProvider>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return null!;
                    default:
                        throw new NotSupportedException(runtimeConfig.DataSource.GetDatabaseTypeNotSupportedMessage());
                }
            });

            services.AddSingleton<CosmosClientProvider>();

            services.AddSingleton<IQueryEngine>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfigPath>>(serviceProvider);
                RuntimeConfig runtimeConfig = runtimeConfigPath.CurrentValue.ConfigValue!;

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosQueryEngine>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlQueryEngine>(serviceProvider);
                    default:
                        throw new NotSupportedException(runtimeConfig.DataSource.GetDatabaseTypeNotSupportedMessage());
                }
            });

            services.AddSingleton<IMutationEngine>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath
                   = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfigPath>>(serviceProvider);
                RuntimeConfig runtimeConfig = runtimeConfigPath.CurrentValue.ConfigValue!;

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosMutationEngine>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlMutationEngine>(serviceProvider);
                    default:
                        throw new NotSupportedException(runtimeConfig.DataSource.GetDatabaseTypeNotSupportedMessage());
                }
            });

            services.AddSingleton<IQueryExecutor>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfigPath>>(serviceProvider);
                RuntimeConfig runtimeConfig = runtimeConfigPath.CurrentValue.ConfigValue!;

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
                            runtimeConfig.DataSource.GetDatabaseTypeNotSupportedMessage());
                }
            });

            services.AddSingleton<IQueryBuilder>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfigPath>>(serviceProvider);
                RuntimeConfig runtimeConfig = runtimeConfigPath.CurrentValue.ConfigValue!;

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
                        throw new NotSupportedException(runtimeConfig.DataSource.GetDatabaseTypeNotSupportedMessage());
                }
            });

            services.AddSingleton<ISqlMetadataProvider>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfigPath>>(serviceProvider);
                RuntimeConfig runtimeConfig = runtimeConfigPath.CurrentValue.ConfigValue!;

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return null!;
                    case DatabaseType.mssql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<MsSqlMetadataProvider>(serviceProvider);
                    case DatabaseType.postgresql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<PostgreSqlMetadataProvider>(serviceProvider);
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<MySqlMetadataProvider>(serviceProvider);
                    default:
                        throw new NotSupportedException(runtimeConfig.DataSource.GetDatabaseTypeNotSupportedMessage());
                }
            });

            services.AddSingleton(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfigPath>>(serviceProvider);
                RuntimeConfig runtimeConfig = runtimeConfigPath.CurrentValue.ConfigValue!;

                switch (runtimeConfig.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return null!;
                    case DatabaseType.mssql:
                        return new DbExceptionParserBase();
                    case DatabaseType.postgresql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<PostgresDbExceptionParser>(serviceProvider);
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<MySqlDbExceptionParser>(serviceProvider);
                    default:
                        throw new NotSupportedException(runtimeConfig.DataSource.GetDatabaseTypeNotSupportedMessage());
                }
            });

            services.AddSingleton<IDocumentHashProvider, Sha256DocumentHashProvider>();
            services.AddSingleton<IDocumentCache, DocumentCache>();
            services.AddSingleton<GraphQLService>();
            services.AddSingleton<RestService>();

            //Enable accessing HttpContext in RestService to get ClaimsPrincipal.
            services.AddHttpContextAccessor();

            ConfigureAuthentication(services);

            services.AddAuthorization();
            services.AddSingleton<IAuthorizationHandler, RequestAuthorizationHandler>();
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath
                = app.ApplicationServices.GetService<IOptionsMonitor<RuntimeConfigPath>>()!;
            runtimeConfigPath.CurrentValue.SetRuntimeConfigValue();
            RuntimeConfig? runtimeConfig = runtimeConfigPath.CurrentValue.ConfigValue;
            bool isRuntimeReady = false;
            if (runtimeConfig is not null)
            {
                isRuntimeReady =
                    PerformOnConfigChangeAsync(app).Result;
            }
            else
            {
                runtimeConfigPath.OnChange(async (newConfig) =>
                {
                    if (!string.IsNullOrWhiteSpace(runtimeConfigPath.CurrentValue.ConfigFileName))
                    {
                        runtimeConfigPath.CurrentValue.SetRuntimeConfigValue();
                        isRuntimeReady =
                            await PerformOnConfigChangeAsync(app);
                    }
                });
            }

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();
            app.Use(async (context, next) =>
            {
                bool isSettingConfig = context.Request.Path.StartsWithSegments("/configuration")
                    && context.Request.Method == HttpMethod.Post.Method;
                if (isRuntimeReady)
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

            // Conditionally add EasyAuth middleware if no JwtAuth configuration supplied.
            if (runtimeConfig is not null && runtimeConfig.IsEasyAuthAuthenticationProvider())
            {
                app.UseEasyAuthMiddleware();
            }
            else
            {
                app.UseJwtAuthenticationMiddleware();
            }

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapBananaCakePop("/graphql");
            });
        }

        /// <summary>
        /// Perform these additional steps once the configuration has been bound
        /// to a particular database type.
        /// </summary>
        /// <param name="app"></param>
        /// <returns>Indicates if the runtime is ready to accept requests.</returns>
        private static async Task<bool> PerformOnConfigChangeAsync(IApplicationBuilder app)
        {
            try
            {
                // Now that the configuration has been set, perform validation of the runtime config
                // itself.
                app.ApplicationServices.GetService<RuntimeConfigValidator>()!.ValidateConfig();

                ISqlMetadataProvider? sqlMetadataProvider =
                    app.ApplicationServices.GetService<ISqlMetadataProvider>();

                if (sqlMetadataProvider is not null)
                {
                    await sqlMetadataProvider.InitializeAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to complete runtime " +
                    $"intialization operations due to: {ex.Message}.");
                return false;
            }
        }

        private void ConfigureAuthentication(IServiceCollection services)
        {
            // Read configuration and use it locally.
            RuntimeConfigPath runtimeConfigPath = Configuration.Get<RuntimeConfigPath>();
            runtimeConfigPath.SetRuntimeConfigValue();
            RuntimeConfig? runtimeConfig = runtimeConfigPath.ConfigValue;

            // Parameterless AddAuthentication() , i.e. No defaultScheme, allows the custom JWT middleware
            // to manually call JwtBearerHandler.HandleAuthenticateAsync() and populate the User if successful.
            // This also enables the custom middleware to send the AuthN failure reason in the challenge header.
            if (runtimeConfig != null &&
                runtimeConfig.AuthNConfig != null &&
                !runtimeConfig.IsEasyAuthAuthenticationProvider())
            {
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Audience = runtimeConfig.AuthNConfig.Jwt!.Audience;
                    options.Authority = runtimeConfig.AuthNConfig.Jwt!.Issuer;
                });
            }
        }
    }
}
