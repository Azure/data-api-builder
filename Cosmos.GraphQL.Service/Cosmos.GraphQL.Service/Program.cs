using System;
using System.IO;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Cosmos.GraphQL.Service.configurations;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Cosmos.GraphQL.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IHost host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureAppConfiguration(config =>
                    config
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional:false)
                    .AddEnvironmentVariables()
                )
                .ConfigureServices((context, services) =>
                {
                    IConfiguration configuration = context.Configuration;

                    services.Configure<DataGatewayConfig>(configuration.GetSection("DatabaseConnection"));
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<DataGatewayConfig>, DataGatewayConfigPostConfiguration>());
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DataGatewayConfig>, DataGatewayConfigValidation>());

                    // Read configuration and use it locally.
                    DataGatewayConfig dataGatewayConfig = new DataGatewayConfig();
                    // Need to rename DatabaseConnection to DataGatewayConfig in the CI pipeline.
                    configuration.Bind("DatabaseConnection", dataGatewayConfig);

                    switch(dataGatewayConfig.DatabaseType)
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
                })
                .Build();

            host.Run();
        }
    }
}
