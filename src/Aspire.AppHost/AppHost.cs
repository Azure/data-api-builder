using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

// string? databaseType = Environment.GetEnvironmentVariable("ASPIRE_DATABASE_TYPE");

// string databaseConnectionString = Environment.GetEnvironmentVariable("ASPIRE_DATABASE_CONNECTION_STRING") ?? string.Empty;

// IResourceBuilder<ProjectResource> dabService = databaseType?.ToLowerInvariant() switch
// {
//     "mssql" => ConfigureSql(builder, databaseConnectionString),
//     "postgresql" => ConfigurePostgres(builder, databaseConnectionString),
//     _ => throw new Exception("Please set the ASPIRE_DATABASE_TYPE environment variable to either 'mssql' or 'postgresql'.")
// };

// ConfigureCosmosConfiguration(builder, dabService);

builder.AddProject<Projects.Azure_DataApiBuilder_Service>("wellbeing-os-data-api");

builder.Build().Run();

// static IResourceBuilder<ProjectResource> ConfigureSql(IDistributedApplicationBuilder builder, string databaseConnectionString)
// {
//     string sqlScript = File.ReadAllText("./init-scripts/sql/create-database.sql");

//     IResourceBuilder<SqlServerDatabaseResource>? sqlDbContainer = null;

//     if (string.IsNullOrEmpty(databaseConnectionString))
//     {
//         Console.WriteLine("No connection string provided, starting a local SQL Server container.");

//         sqlDbContainer = builder.AddSqlServer("sqlserver")
//             .WithDataVolume()
//             .WithLifetime(ContainerLifetime.Persistent)
//             .AddDatabase("msSqlDb", "Trek")
//             .WithCreationScript(sqlScript);
//     }

//     IResourceBuilder<ProjectResource> mssqlService = builder.AddProject<Projects.Azure_DataApiBuilder_Service>("mssql-service", "Development")
//         .WithArgs("-f", "net10.0")
//         .WithEndpoint(endpointName: "https", e => e.Port = 1234)
//         .WithEndpoint(endpointName: "http", e => e.Port = 2345)
//         .WithEnvironment("db-type", "mssql")
//         .WithUrls(e =>
//         {
//             e.Urls.Clear();
//             e.Urls.Add(new() { Url = "/swagger", DisplayText = "🔒Swagger", Endpoint = e.GetEndpoint("https") });
//             e.Urls.Add(new() { Url = "/graphql", DisplayText = "🔒GraphQL", Endpoint = e.GetEndpoint("https") });
//         })
//         .WithHttpHealthCheck("/health");

//     if (sqlDbContainer is null)
//     {
//         mssqlService.WithEnvironment("ConnectionStrings__Database", databaseConnectionString);
//     }
//     else
//     {
//         mssqlService.WithEnvironment("ConnectionStrings__Database", sqlDbContainer)
//             .WaitFor(sqlDbContainer);
//     }

//     return mssqlService;
// }

// static IResourceBuilder<ProjectResource> ConfigurePostgres(IDistributedApplicationBuilder builder, string databaseConnectionString)
// {
//     string pgScript = File.ReadAllText("./init-scripts/pg/create-database-pg.sql");

//     IResourceBuilder<PostgresDatabaseResource>? postgresDb = null;

//     if (string.IsNullOrEmpty(databaseConnectionString))
//     {
//         Console.WriteLine("No connection string provided, starting a local PostgreSQL container.");

//         postgresDb = builder.AddPostgres("postgres")
//             .WithPgAdmin()
//             .WithLifetime(ContainerLifetime.Persistent)
//             .AddDatabase("pgDb", "postgres")
//             .WithCreationScript(pgScript);
//     }

//     IResourceBuilder<ProjectResource> pgService = builder.AddProject<Projects.Azure_DataApiBuilder_Service>("pg-service", "Development")
//         .WithArgs("-f", "net10.0")
//         .WithEndpoint(endpointName: "https", e => e.Port = 1234)
//         .WithEndpoint(endpointName: "http", e => e.Port = 2345)
//         .WithEnvironment("db-type", "postgresql")
//         .WithUrls(e =>
//         {
//             e.Urls.Clear();
//             e.Urls.Add(new() { Url = "/swagger", DisplayText = "🔒Swagger", Endpoint = e.GetEndpoint("https") });
//             e.Urls.Add(new() { Url = "/graphql", DisplayText = "🔒GraphQL", Endpoint = e.GetEndpoint("https") });
//         })
//         .WithHttpHealthCheck("/health");

//     if (postgresDb is null)
//     {
//         pgService.WithEnvironment("ConnectionStrings__Database", databaseConnectionString);
//     }
//     else
//     {
//         pgService.WithEnvironment("ConnectionStrings__Database", postgresDb)
//             .WaitFor(postgresDb);
//     }

//     return pgService;
// }

// static void ConfigureCosmosConfiguration(IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> service)
// {
//     bool useCosmosConfig = string.Equals(Environment.GetEnvironmentVariable("ASPIRE_USE_COSMOS_CONFIG"), "true", StringComparison.OrdinalIgnoreCase);

//     if (!useCosmosConfig)
//     {
//         return;
//     }

//     var cosmosAccount = builder.AddAzureCosmosDB("cosmosdb")
//         .RunAsPreviewEmulator(c =>
//         {
//             c.WithImagePullPolicy(ImagePullPolicy.Always);
//             c.WithLifetime(ContainerLifetime.Persistent);
//             //c.WithHttpsEndpoint(targetPort: 8081);
//             c.WithGatewayPort(8081);
//             c.WithDataExplorer();
//         });

//     string cosmosDatabaseName = Environment.GetEnvironmentVariable("ASPIRE_COSMOS_DATABASE") ?? "dab-config";
//     var cosmosDatabase = cosmosAccount.AddCosmosDatabase(cosmosDatabaseName);

//     string cosmosContainerName = Environment.GetEnvironmentVariable("ASPIRE_COSMOS_CONTAINER") ?? "configurations";
//     var cosmosContainer = cosmosDatabase.AddContainer(cosmosContainerName, partitionKeyPath: "/environment");

//     string cosmosDocumentId = Environment.GetEnvironmentVariable("ASPIRE_COSMOS_DOCUMENT_ID") ?? "runtime-config";
//     string cosmosPartitionKey = Environment.GetEnvironmentVariable("ASPIRE_COSMOS_PARTITION_KEY") ?? "production";
//     string cosmosConnectionMode = Environment.GetEnvironmentVariable("ASPIRE_COSMOS_CONNECTION_MODE") ?? "Gateway";


//     service.WaitFor(cosmosContainer)
//         .WithEnvironment("ConnectionStrings__ConfigurationCdb", cosmosAccount)
//         .WithEnvironment("DAB_CONFIG_SOURCE", "cosmosdb")
//         .WithEnvironment("DAB_COSMOS_DATABASE", cosmosDatabaseName)
//         .WithEnvironment("DAB_COSMOS_CONTAINER", cosmosContainerName)
//         .WithEnvironment("DAB_COSMOS_DOCUMENT_ID", cosmosDocumentId)
//         .WithEnvironment("DAB_COSMOS_PARTITION_KEY", cosmosPartitionKey)
//         .WithEnvironment("DAB_COSMOS_CONNECTION_MODE", cosmosConnectionMode)
//         .WithEnvironment("DAB_COSMOS_BYPASS_CERT_VALIDATION", "true");

//     if (int.TryParse(Environment.GetEnvironmentVariable("ASPIRE_COSMOS_POLLING_INTERVAL"), out int pollingInterval) && pollingInterval > 0)
//     {
//         service.WithEnvironment("DAB_COSMOS_POLLING_INTERVAL", pollingInterval.ToString());
//     }
// }
