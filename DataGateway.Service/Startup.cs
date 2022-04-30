using System;
using System.Collections.Generic;
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
            RuntimeConfigPath options = new();
            Configuration.Bind(RuntimeConfigPath.CONFIGFILE_PROPERTY_NAME, options);
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            string runtimeConfigJson =
                Configuration.GetValue<string>(RuntimeConfigPath.GetFileNameAsPerEnvironment());
            services.AddSingleton()
            if (Configuration is IConfigurationRoot root)
            {
                if (root.Providers.First(prov => prov is InMemoryUpdateableConfigurationProvider) is InMemoryUpdateableConfigurationProvider provider)
                {
                    services.AddSingleton(provider);
                    _inMemoryConfigChangeToken = provider.GetReloadToken();
                    _inMemoryConfigChangeToken.RegisterChangeCallback(new Action<object>(OnConfigurationChanged), provider);
                }
            }

            services.AddSingleton<IGraphQLMetadataProvider, GraphQLFileMetadataProvider>();
            services.AddSingleton<CosmosClientProvider>();

            services.AddSingleton<IQueryEngine>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfig> runtimeConfig
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfig>>(serviceProvider);
                switch (runtimeConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosQueryEngine>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlQueryEngine>(serviceProvider);
                    default:
                        throw new NotSupportedException(string.Format("The provided Database_Type value: {0} is currently not supported." +
                            "Please check the configuration file.", runtimeConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IMutationEngine>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfig> runtimeConfig
                                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfig>>(serviceProvider);
                switch (runtimeConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosMutationEngine>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlMutationEngine>(serviceProvider);
                    default:
                        throw new NotSupportedException(string.Format("The provided Database_Type value: {0} is currently not supported." +
                            "Please check the configuration file.", runtimeConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IConfigValidator>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfig> runtimeConfig
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfig>>(serviceProvider);
                switch (runtimeConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosConfigValidator>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlConfigValidator>(serviceProvider);
                    default:
                        throw new NotSupportedException(string.Format("The provided Database_Type value: {0} is currently not supported." +
                            "Please check the configuration file.", runtimeConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IQueryExecutor>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfig> runtimeConfig
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfig>>(serviceProvider);
                switch (runtimeConfig.CurrentValue.DatabaseType)
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
                        throw new NotSupportedException(string.Format("The provided Database_Type value: {0} is currently not supported." +
                            "Please check the configuration file.", runtimeConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IQueryBuilder>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfig> runtimeConfig
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfig>>(serviceProvider);
                switch (runtimeConfig.CurrentValue.DatabaseType)
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
                        throw new NotSupportedException(string.Format("The provided Database_Type value: {0} is currently not supported." +
                            "Please check the configuration file.", runtimeConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<ISqlMetadataProvider>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfig> runtimeConfig
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfig>>(serviceProvider);
                switch (runtimeConfig.CurrentValue.DatabaseType)
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
                        throw new NotSupportedException(string.Format("The provided Database_Type value: {0} is currently not supported." +
                            "Please check the configuration file.", runtimeConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<RuntimeConfig> runtimeConfig
                    = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<RuntimeConfig>>(serviceProvider);
                switch (runtimeConfig.CurrentValue.DatabaseType)
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
                        throw new NotSupportedException(String.Format("The provided Database_Type value: {0} is currently not supported." +
                            "Please check the configuration file.", runtimeConfig.CurrentValue.DatabaseType));
                }
            });

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<RuntimeConfig>, RuntimeConfigPostConfiguration>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RuntimeConfig>, RuntimeConfigValidation>());

            services.AddSingleton<IDocumentHashProvider, Sha256DocumentHashProvider>();
            services.AddSingleton<IDocumentCache, DocumentCache>();
            services.AddSingleton<GraphQLService>();
            services.AddSingleton<RestService>();

            //Enable accessing HttpContext in RestService to get ClaimsPrincipal.
            services.AddHttpContextAccessor();

            // Parameterless AddAuthentication() , i.e. No defaultScheme, allows the custom JWT middleware
            // to manually call JwtBearerHandler.HandleAuthenticateAsync() and populate the User if successful.
            // This also enables the custom middleware to send the AuthN failure reason in the challenge header.
            if (runtimeConfig.AuthNConfig != null &&
                !runtimeConfig.IsEasyAuthAuthenticationProvider())
            {
                services.AddAuthentication()
                .AddJwtBearer(options =>
                {
                    options.Audience = runtimeConfig.AuthNConfig.Jwt!.Audience;
                    options.Authority = runtimeConfig.AuthNConfig.Jwt!.Issuer;
                });
            }

            services.AddAuthorization();
            services.AddSingleton<IAuthorizationHandler, RequestAuthorizationHandler>();
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            IOptionsMonitor<RuntimeConfig> runtimeConfig
                = app.ApplicationServices.GetService<IOptionsMonitor<RuntimeConfig>>()!;
            bool isRuntimeReady = false;
            if (runtimeConfig.CurrentValue.DoesDatabaseTypeHaveValue())
            {
                isRuntimeReady =
                    PerformOnConfigChangeAsync(app).Result;
            }
            else
            {
                runtimeConfig.OnChange(async (newConfig) =>
                {
                    isRuntimeReady =
                        await PerformOnConfigChangeAsync(app);
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
                IOptionsMonitor<RuntimeConfig>? runtimeConfig
                    = context.RequestServices.GetService<IOptionsMonitor<RuntimeConfig>>();

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
            if (runtimeConfig.CurrentValue.IsEasyAuthAuthenticationProvider())
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

        private RuntimeConfig GetRunTimeConfig()
        {
            RuntimeConfig runtimeConfig;
            string runtimeConfigFileName =
                Configuration.GetSection(RuntimeConfig.CONFIGFILE_PROPERTY_NAME).Get<string>();
            if (runtimeConfigFileName != default)
            {
                runtimeConfig = RuntimeConfig.GetDeserializedConfig<RuntimeConfig>(runtimeConfigFileName);
            }
            else
            {
                // Since the runtime config has no name at the root level that encapsulates
                // all these sections - get them separately and
                // create a new "RuntimeConfig" using these sections.
                string schemaName = Configuration.GetValue<string>(RuntimeConfig.SCHEMA_PROPERTY_NAME);
                IConfigurationSection? dataSourceSection = Configuration.GetSection(DataSource.CONFIG_PROPERTY_NAME);
                DataSource dataSource = dataSourceSection.Get<DataSource>();
                CosmosDbOptions? cosmosDbOptions =
                    Configuration.GetSection(CosmosDbOptions.CONFIG_PROPERTY_NAME).Get<CosmosDbOptions>();
                MsSqlOptions? msSqlOptions =
                    Configuration.GetSection(MsSqlOptions.CONFIG_PROPERTY_NAME).Get<MsSqlOptions>();
                PostgreSqlOptions? postgreSqlOptions =
                    Configuration.GetSection(PostgreSqlOptions.CONFIG_PROPERTY_NAME).Get<PostgreSqlOptions>();
                MySqlOptions? mysqlOptions =
                    Configuration.GetSection(MySqlOptions.CONFIG_PROPERTY_NAME).Get<MySqlOptions>();
                Dictionary<GlobalSettingsType, object>? runtimeSettings =
                    Configuration.GetValue<Dictionary<GlobalSettingsType, object>>(GlobalSettings.CONFIG_PROPERTY_NAME);
                Dictionary<string, Entity> entities =
                    Configuration.GetSection(Entity.CONFIG_PROPERTY_NAME)
                        .Get<Dictionary<string, Entity>>();

                runtimeConfig = new(
                    schemaName,
                    dataSource,
                    cosmosDbOptions,
                    msSqlOptions,
                    postgreSqlOptions,
                    mysqlOptions,
                    runtimeSettings,
                    entities);
            }

            //runtimeConfig.SetDefaults();
            return runtimeConfig;
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
                ISqlMetadataProvider? sqlMetadataProvider =
                    app.ApplicationServices.GetService<ISqlMetadataProvider>();

                if (sqlMetadataProvider is not null)
                {
                    await sqlMetadataProvider.InitializeAsync();
                }

                // Now that the configuration has been set, perform validation.
                app.ApplicationServices.GetService<IConfigValidator>()!.ValidateConfig();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to complete runtime " +
                    $"intialization operations due to: {ex.Message}.");
                return false;
            }
        }
    }
}
