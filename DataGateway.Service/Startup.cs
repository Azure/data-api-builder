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
            DataGatewayConfig dataGatewayConfig = new();
            Configuration.Bind(nameof(DataGatewayConfig), dataGatewayConfig);
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<DataGatewayConfig>(Configuration.GetSection(nameof(DataGatewayConfig)));

            // Read configuration and use it locally.
            DataGatewayConfig dataGatewayConfig = new();
            Configuration.Bind(nameof(DataGatewayConfig), dataGatewayConfig);

            if (Configuration is IConfigurationRoot root)
            {
                if (root.Providers.First(prov => prov is InMemoryUpdateableConfigurationProvider) is InMemoryUpdateableConfigurationProvider provider)
                {
                    services.AddSingleton(provider);
                    _inMemoryConfigChangeToken = provider.GetReloadToken();
                    _inMemoryConfigChangeToken.RegisterChangeCallback(new Action<object>(OnConfigurationChanged), provider);
                }
            }

            services.AddSingleton<IRuntimeConfigProvider, RuntimeConfigProvider>();

            services.AddSingleton<IGraphQLMetadataProvider, GraphQLFileMetadataProvider>();
            services.AddSingleton<CosmosClientProvider>();

            services.AddSingleton<IQueryEngine>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosQueryEngine>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlQueryEngine>(serviceProvider);
                    default:
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IMutationEngine>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosMutationEngine>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlMutationEngine>(serviceProvider);
                    default:
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IConfigValidator>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosConfigValidator>(serviceProvider);
                    case DatabaseType.mssql:
                    case DatabaseType.postgresql:
                    case DatabaseType.mysql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlConfigValidator>(serviceProvider);
                    default:
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IQueryExecutor>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
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
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IQueryBuilder>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
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
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<ISqlMetadataProvider>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig =
                    ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
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
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
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
                        throw new NotSupportedException(String.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<DataGatewayConfig>, DataGatewayConfigPostConfiguration>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DataGatewayConfig>, DataGatewayConfigValidation>());

            services.AddSingleton<IDocumentHashProvider, Sha256DocumentHashProvider>();
            services.AddSingleton<IDocumentCache, DocumentCache>();
            services.AddSingleton<GraphQLService>();
            services.AddSingleton<RestService>();

            //Enable accessing HttpContext in RestService to get ClaimsPrincipal.
            services.AddHttpContextAccessor();

            // Parameterless AddAuthentication() , i.e. No defaultScheme, allows the custom JWT middleware
            // to manually call JwtBearerHandler.HandleAuthenticateAsync() and populate the User if successful.
            // This also enables the custom middleware to send the AuthN failure reason in the challenge header.
            if (dataGatewayConfig.Authentication.Provider != "EasyAuth")
            {
                services.AddAuthentication()
                    .AddJwtBearer(options =>
                    {
                        options.Audience = dataGatewayConfig.Authentication.Audience;
                        options.Authority = dataGatewayConfig.Authentication.Issuer;
                    });
            }

            services.AddAuthorization();
            services.AddSingleton<IAuthorizationHandler, RequestAuthorizationHandler>();
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = app.ApplicationServices.GetService<IOptionsMonitor<DataGatewayConfig>>()!;
            bool isRuntimeReady = false;
            if (dataGatewayConfig.CurrentValue.DatabaseType.HasValue)
            {
                isRuntimeReady =
                    PerformOnConfigChangeAsync(app).Result;
            }
            else
            {
                dataGatewayConfig.OnChange(async (newConfig) =>
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
                IOptionsMonitor<DataGatewayConfig>? dataGatewayConfig = context.RequestServices.GetService<IOptionsMonitor<DataGatewayConfig>>();

                bool isSettingConfig = context.Request.Path.StartsWithSegments("/configuration") && context.Request.Method == HttpMethod.Post.Method;
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
            if (dataGatewayConfig.CurrentValue.Authentication.Provider == "EasyAuth")
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
