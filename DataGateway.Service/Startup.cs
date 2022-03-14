using System;
using System.Linq;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
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
        private IChangeToken _phoenixConfigChangeToken;

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
                PhoenixConfigurationProvider? phoenixProvider = root.Providers.First(prov => prov is PhoenixConfigurationProvider) as PhoenixConfigurationProvider;
                if (phoenixProvider != null)
                {
                    services.AddSingleton(phoenixProvider);
                    _phoenixConfigChangeToken = phoenixProvider.GetReloadToken();
                    _phoenixConfigChangeToken.RegisterChangeCallback(new Action<object>(OnConfigurationChanged), phoenixProvider);
                }
            }

            services.AddSingleton<IMetadataStoreProvider>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> monitor = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                if (string.IsNullOrEmpty(monitor.CurrentValue.ResolverConfigFile))
                {
                    return new PhoenixMetadataStoreProvider(monitor);
                }
                else
                {
                    return new FileMetadataStoreProvider(monitor);
                }
            });

            services.AddSingleton(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                if (dataGatewayConfig.CurrentValue.DatabaseType != DatabaseType.Cosmos)
                {
                    throw new InvalidOperationException("We shouldn't need a CosmosClientProvider if we're not accessing a CosmosDb");
                }

                return new CosmosClientProvider(dataGatewayConfig);
            });

            services.AddSingleton<IQueryEngine>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.Cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosQueryEngine>(serviceProvider);
                    case DatabaseType.MsSql:
                    case DatabaseType.PostgreSql:
                    case DatabaseType.MySql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlQueryEngine>(serviceProvider);
                    default:
                        throw new NotSupportedException(String.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IMutationEngine>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.Cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosMutationEngine>(serviceProvider);
                    case DatabaseType.MsSql:
                    case DatabaseType.PostgreSql:
                    case DatabaseType.MySql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlMutationEngine>(serviceProvider);
                    default:
                        throw new NotSupportedException(String.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IConfigValidator>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.Cosmos:
                        return ActivatorUtilities.GetServiceOrCreateInstance<CosmosConfigValidator>(serviceProvider);
                    case DatabaseType.MsSql:
                    case DatabaseType.PostgreSql:
                    case DatabaseType.MySql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlConfigValidator>(serviceProvider);
                    default:
                        throw new NotSupportedException(String.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IQueryExecutor>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.Cosmos:
                        return null!;
                    case DatabaseType.MsSql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<QueryExecutor<SqlConnection>>(serviceProvider);
                    case DatabaseType.PostgreSql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<QueryExecutor<NpgsqlConnection>>(serviceProvider);
                    case DatabaseType.MySql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<QueryExecutor<MySqlConnection>>(serviceProvider);
                    default:
                        throw new NotSupportedException(String.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IQueryBuilder>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.Cosmos:
                        return null!;
                    case DatabaseType.MsSql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<MsSqlQueryBuilder>(serviceProvider);
                    case DatabaseType.PostgreSql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<PostgresQueryBuilder>(serviceProvider);
                    case DatabaseType.MySql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<MySqlQueryBuilder>(serviceProvider);
                    default:
                        throw new NotSupportedException(String.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<DataGatewayConfig>, DataGatewayConfigPostConfiguration>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DataGatewayConfig>, DataGatewayConfigValidation>());

            services.AddSingleton<GraphQLService>();
            services.AddSingleton<RestService>();

            //Enable accessing HttpContext in RestService to get ClaimsPrincipal.
            services.AddHttpContextAccessor();
            services.AddAuthorization();

            services.AddSingleton<IAuthorizationHandler, RequestAuthorizationHandler>();
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // validate the configuration after the services have been built
            // but before the application is built
            //app.ApplicationServices.GetService<IConfigValidator>()!.ValidateConfig();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();
            app.Use(async (context, next) =>
            {
                IOptionsMonitor<DataGatewayConfig>? dataGatewayConfig = context.RequestServices.GetService<IOptionsMonitor<DataGatewayConfig>>();

                bool isConfigSetup = (dataGatewayConfig != null && dataGatewayConfig.CurrentValue.DatabaseType != DatabaseType.None);
                bool isConfigPath = context.Request.Path.StartsWithSegments("/configuration") && context.Request.Method == HttpMethods.Post;

                if (isConfigSetup)
                {
                    await next.Invoke();
                }
                else if (isConfigPath)
                {
                    await next.Invoke();
                }
                else
                {
                    context.Response.StatusCode = 503;
                }
            });
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapBananaCakePop("/graphql");
            });
        }
    }
}
