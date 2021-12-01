using Azure.DataGateway.Service.configurations;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using System;
using System.Threading.Tasks;

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
            var dataGatewayConfig = new DataGatewayConfig();
            config.Bind(nameof(DataGatewayConfig), dataGatewayConfig);

            switch (dataGatewayConfig.DatabaseType)
            {
                case DatabaseType.Cosmos:
                    services.AddSingleton<CosmosClientProvider, CosmosClientProvider>();
                    services.AddSingleton<IMetadataStoreProvider, FileMetadataStoreProvider>();
                    services.AddSingleton<IQueryEngine, CosmosQueryEngine>();
                    services.AddSingleton<IMutationEngine, CosmosMutationEngine>();
                    break;
                case DatabaseType.MsSql:
                    services.AddSingleton<IMetadataStoreProvider, FileMetadataStoreProvider>();
                    services.AddSingleton<IQueryExecutor, QueryExecutor<SqlConnection>>();
                    services.AddSingleton<IQueryBuilder, MsSqlQueryBuilder>();
                    services.AddSingleton<IQueryEngine, SqlQueryEngine>();
                    services.AddSingleton<IMutationEngine, SqlMutationEngine>();
                    break;
                case DatabaseType.PostgreSql:
                    services.AddSingleton<IMetadataStoreProvider, FileMetadataStoreProvider>();
                    services.AddSingleton<IQueryExecutor, QueryExecutor<NpgsqlConnection>>();
                    services.AddSingleton<IQueryBuilder, PostgresQueryBuilder>();
                    services.AddSingleton<IQueryEngine, SqlQueryEngine>();
                    services.AddSingleton<IMutationEngine, SqlMutationEngine>();
                    break;
                default:
                    throw new NotSupportedException(String.Format("The provided DatabaseType value: {0} is currently not supported." +
                        "Please check the configuration file.", dataGatewayConfig.DatabaseType));
            }

            services.AddSingleton<GraphQLService, GraphQLService>();

            Task.Run(async () =>
            {
                await new ConnectToContainerGateway().RunAsync(new System.Threading.CancellationToken(), config);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            var webSocketOptions = new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromSeconds(3)
            };
            app.UseWebSockets(webSocketOptions);

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapBananaCakePop("/graphql");
            });
        }
    }
}
