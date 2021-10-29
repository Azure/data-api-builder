using System;
using Azure.DataGateway.Service.configurations;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using Npgsql;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            services.Configure<DataGatewayConfig>(Configuration.GetSection(nameof(DataGatewayConfig)));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<DataGatewayConfig>, DataGatewayConfigPostConfiguration>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DataGatewayConfig>, DataGatewayConfigValidation>());

            // Read configuration and use it locally.
            DataGatewayConfig dataGatewayConfig = new DataGatewayConfig();
            Configuration.Bind(nameof(DataGatewayConfig), dataGatewayConfig);

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
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

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
