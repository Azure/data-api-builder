using System;
using Azure.DataGateway.Service.AuthenticationHelpers;
using System.Linq;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

            services.AddSingleton<IMetadataStoreProvider, FileMetadataStoreProvider>();
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

            // Parameterless AddAuthentication() , i.e. No defaultScheme, allows the custom JWT middleware
            // to manually call JwtBearerHandler.HandleAuthenticateAsync() and populate the User if successful.
            // This also enables the custom middleware to send the AuthN failure reason in the challenge header.
            if (dataGatewayConfig.JwtAuth != null)
            {
                services.AddAuthentication()
                    .AddJwtBearer(options =>
                    {
                        options.Audience = dataGatewayConfig.JwtAuth.Audience;
                        options.Authority = dataGatewayConfig.JwtAuth.Issuer;
                    });
            }

            services.AddAuthorization();
            services.AddSingleton<IAuthorizationHandler, RequestAuthorizationHandler>();
            services.AddControllers();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            IOptionsMonitor<DataGatewayConfig>? dataGatewayConfig = app.ApplicationServices.GetService<IOptionsMonitor<DataGatewayConfig>>();
            if (dataGatewayConfig != null && dataGatewayConfig.CurrentValue.DatabaseType.HasValue)
            {
                // If the configuration has been set, validate it after the services have been built but
                // before the application is built. If it hasn't been set yet, skip validation, it will
                // happen when the config changes.
                app.ApplicationServices.GetService<IConfigValidator>()!.ValidateConfig();
            }
            else
            {
                dataGatewayConfig.OnChange((newConfig) =>
                {
                    app.ApplicationServices.GetService<IConfigValidator>()!.ValidateConfig();
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

                bool isConfigSetup = (dataGatewayConfig != null && dataGatewayConfig.CurrentValue.DatabaseType.HasValue);
                bool isConfigPath = context.Request.Path.StartsWithSegments("/configuration");

                if (isConfigSetup || isConfigPath)
                {
                    await next.Invoke();
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                }
            });
            app.UseAuthentication();

            // Conditionally add EasyAuth middleware if no JwtAuth configuration supplied.
            // If EasyAuth not present, this will result in all requests being anonymous.
            if (dataGatewayConfig != null && dataGatewayConfig.CurrentValue.JwtAuth == null)
            {
                app.UseEasyAuth();
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
    }
}
