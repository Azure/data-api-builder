using System;
using Cosmos.GraphQL.Service.configurations;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Cosmos.GraphQL.Service
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
            if (Configuration.GetValue<string>("DatabaseConnection:DatabaseType") is null)
            {
                throw new NotSupportedException(String.Format("The configuration file is invalid and does not *contain* the DatabaseType key."));
            }

            DatabaseType dbType = configurations.ConfigurationProvider.getInstance().DbType;

            switch (dbType)
            {
                case DatabaseType.Cosmos:
                    services.AddSingleton<IClientProvider<CosmosClient>, CosmosClientProvider>();
                    services.AddSingleton<CosmosClientProvider, CosmosClientProvider>();
                    services.AddSingleton<DocumentMetadataStoreProvider, DocumentMetadataStoreProvider>();
                    services.AddSingleton<IMetadataStoreProvider, CachedMetadataStoreProvider>();
                    services.AddSingleton<IQueryEngine, CosmosQueryEngine>();
                    services.AddSingleton<IMutationEngine, CosmosMutationEngine>();
                    break;
                case DatabaseType.MsSql:
                    services.AddSingleton<IDbConnectionService, MsSqlClientProvider>();
                    services.AddSingleton<IMetadataStoreProvider, FileMetadataStoreProvider>();
                    services.AddSingleton<IQueryExecutor, QueryExecutor>();
                    services.AddSingleton<IQueryBuilder, MsSqlQueryBuilder>();
                    services.AddSingleton<IQueryEngine, SqlQueryEngine<SqlParameter>>();
                    services.AddSingleton<IMutationEngine, SqlMutationEngine>();
                    break;
                case DatabaseType.PostgreSql:
                    services.AddSingleton<IDbConnectionService, PostgresClientProvider>();
                    services.AddSingleton<IMetadataStoreProvider, FileMetadataStoreProvider>();
                    services.AddSingleton<IQueryExecutor, QueryExecutor>();
                    services.AddSingleton<IQueryBuilder, PostgresQueryBuilder>();
                    services.AddSingleton<IQueryEngine, SqlQueryEngine<NpgsqlParameter>>();
                    services.AddSingleton<IMutationEngine, SqlMutationEngine>();
                    break;
                default:
                    throw new NotSupportedException(String.Format("The provided DatabaseType value: {0} is currently not supported." +
                        "Please check the configuration file.", dbType));
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

            // use graphiql at default url /ui/graphiql
            app.UseGraphQLGraphiQL();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
