using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace Cosmos.GraphQL.Service
{
    enum DatabaseType
    {
        MsSql, 
        Cosmos, 
        Postgres,
    }

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
            if(Configuration.GetValue<string>("DatabaseConnection:DatabaseType") is null)
            {
                throw new NotSupportedException(String.Format("The configuration file is invalid and does not *contain* the DatabaseType key."));
            }

            if (!Enum.TryParse<DatabaseType>(Configuration.GetValue<string>("DatabaseConnection:DatabaseType"), out DatabaseType dbType))
            {
                throw new NotSupportedException(String.Format("The configuration file is invalid and does not contain a *valid* DatabaseType key."));
            }
            
            switch (dbType)
            {
                case DatabaseType.Cosmos:
                    services.AddSingleton<IClientProvider<CosmosClient>, CosmosClientProvider>();
                    services.AddSingleton<CosmosClientProvider, CosmosClientProvider>();
                    services.AddSingleton<DocumentMetadataStoreProvider, DocumentMetadataStoreProvider>();
                    break;
                case DatabaseType.MsSql:
                    services.AddSingleton<IClientProvider<SqlConnection>, MSSQLClientProvider>();
                    break;
                default:
                    throw new NotSupportedException(String.Format("The provide enum value: {0} is currently not supported. Please check the configuration file.", dbType));
            }

            services.AddSingleton<IMetadataStoreProvider, CachedMetadataStoreProvider>();
            services.AddSingleton<QueryEngine, QueryEngine>();
            services.AddSingleton<MutationEngine, MutationEngine>();
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
