using System;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            DoConfigureServices(services, Configuration);
            services.AddControllers();
        }

        /// <summary>
        /// This method adds services that are used when running this project or the
        /// functions project. Any services that are required should be added here, unless
        /// it is only required for one or the other.
        /// </summary>
        /// <param name="services">The service collection to which services will be added.</param>
        /// <param name="config">The applications configuration.</param>
        public static void DoConfigureServices(IServiceCollection services, IConfiguration config)
        {
            services.Configure<DataGatewayConfig>(config.GetSection(nameof(DataGatewayConfig)));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<DataGatewayConfig>, DataGatewayConfigPostConfiguration>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DataGatewayConfig>, DataGatewayConfigValidation>());

            // Read configuration and use it locally.
            DataGatewayConfig dataGatewayConfig = new();
            config.Bind(nameof(DataGatewayConfig), dataGatewayConfig);

            switch (dataGatewayConfig.DatabaseType)
            {
                if (root.Providers.First(prov => prov is InMemoryUpdateableConfigurationProvider) is InMemoryUpdateableConfigurationProvider provider)
                {
                    services.AddSingleton(provider);
                    _inMemoryConfigChangeToken = provider.GetReloadToken();
                    _inMemoryConfigChangeToken.RegisterChangeCallback(new Action<object>(OnConfigurationChanged), provider);
                }
            }

            services.AddSingleton<ISqlMetadataProvider>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig =
                    ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.Cosmos:
                        return null!;
                    case DatabaseType.MsSql:
                        return ActivatorUtilities.
                            GetServiceOrCreateInstance<MsSqlMetadataProvider>(serviceProvider);
                    case DatabaseType.PostgreSql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<PostgreSqlMetadataProvider>(serviceProvider);
                    case DatabaseType.MySql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<MySqlMetadataProvider>(serviceProvider);
                    default:
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IGraphQLMetadataProvider>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.Cosmos:
                        return ActivatorUtilities.
                            GetServiceOrCreateInstance<CosmosGraphQLFileMetadataProvider>(serviceProvider);
                    case DatabaseType.MsSql:
                    case DatabaseType.PostgreSql:
                    case DatabaseType.MySql:
                    return ActivatorUtilities.
                        GetServiceOrCreateInstance<SqlGraphQLFileMetadataProvider>(serviceProvider);
                    default:
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });
            services.AddSingleton<CosmosClientProvider>();

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
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
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
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
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
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
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
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
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
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
                            "Please check the configuration file.", dataGatewayConfig.CurrentValue.DatabaseType));
                }
            });

            services.AddSingleton<IHostedService>(implementationFactory: (serviceProvider) =>
            {
                IOptionsMonitor<DataGatewayConfig> dataGatewayConfig = ActivatorUtilities.GetServiceOrCreateInstance<IOptionsMonitor<DataGatewayConfig>>(serviceProvider);
                switch (dataGatewayConfig.CurrentValue.DatabaseType)
                {
                    case DatabaseType.Cosmos:
                        return null!;
                    case DatabaseType.MsSql:
                    case DatabaseType.PostgreSql:
                    case DatabaseType.MySql:
                        return ActivatorUtilities.GetServiceOrCreateInstance<SqlHostedService>(serviceProvider);
                    default:
                        throw new NotSupportedException(string.Format("The provided DatabaseType value: {0} is currently not supported." +
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
            app.ApplicationServices.GetService<IConfigValidator>()!.ValidateConfig();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

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
