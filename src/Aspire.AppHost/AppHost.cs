using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

var aspireDB = Environment.GetEnvironmentVariable("ASPIRE_DATABASE_TYPE");

var databaseConnectionString = Environment.GetEnvironmentVariable("ASPIRE_DATABASE_CONNECTION_STRING") ?? "";

switch (aspireDB)
{
    case "mssql":
        var sqlScript = File.ReadAllText("./init-scripts/sql/create-database.sql");

        IResourceBuilder<SqlServerDatabaseResource>? sqlDbContainer = null;

        if (string.IsNullOrEmpty(databaseConnectionString))
        {
            Console.WriteLine("No connection string provided, starting a local SQL Server container.");

            sqlDbContainer = builder.AddSqlServer("sqlserver")
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent)
                .AddDatabase("msSqlDb", "Trek")
                .WithCreationScript(sqlScript);
        }

        var mssqlService = builder.AddProject<Projects.Azure_DataApiBuilder_Service>("mssql-service", "Development")
            .WithArgs("-f", "net8.0")
            .WithEndpoint(endpointName: "https", (e) => e.Port = 1234)
            .WithEndpoint(endpointName: "http", (e) => e.Port = 2345)
            .WithEnvironment("db-type", "mssql")
            .WithUrls((e) =>
            {
                e.Urls.Clear();
                e.Urls.Add(new() { Url = "/swagger", DisplayText = "🔒Swagger", Endpoint = e.GetEndpoint("https") });
                e.Urls.Add(new() { Url = "/graphql", DisplayText = "🔒GraphQL", Endpoint = e.GetEndpoint("https") });
            })
            .WithHttpHealthCheck("/health");

        if (sqlDbContainer is null)
        {
            mssqlService.WithEnvironment("ConnectionStrings__Database", databaseConnectionString);
        }
        else
        {
            mssqlService.WithEnvironment("ConnectionStrings__Database", sqlDbContainer)
                .WaitFor(sqlDbContainer);
        }

        break;
    case "postgresql":
        var pgScript = File.ReadAllText("./init-scripts/pg/create-database-pg.sql");

        IResourceBuilder<PostgresDatabaseResource>? postgresDB = null;

        if (!string.IsNullOrEmpty(databaseConnectionString))
        {
            Console.WriteLine("No connection string provided, starting a local PostgreSQL container.");

            postgresDB = builder.AddPostgres("postgres")
                .WithPgAdmin()
                .WithLifetime(ContainerLifetime.Persistent)
                .AddDatabase("pgDb", "postgres")
                .WithCreationScript(pgScript);
        }

        var pgService = builder.AddProject<Projects.Azure_DataApiBuilder_Service>("pg-service", "Development")
            .WithArgs("-f", "net8.0")
            .WithEndpoint(endpointName: "https", (e) => e.Port = 1234)
            .WithEndpoint(endpointName: "http", (e) => e.Port = 2345)
            .WithEnvironment("db-type", "postgresql")
            .WithUrls((e) =>
            {
                e.Urls.Clear();
                e.Urls.Add(new() { Url = "/swagger", DisplayText = "🔒Swagger", Endpoint = e.GetEndpoint("https") });
                e.Urls.Add(new() { Url = "/graphql", DisplayText = "🔒GraphQL", Endpoint = e.GetEndpoint("https") });
            })
            .WithHttpHealthCheck("/health");

        if (postgresDB is null)
        {
            pgService.WithEnvironment("ConnectionStrings__Database", databaseConnectionString);
        }
        else
        {
            pgService.WithEnvironment("ConnectionStrings__Database", postgresDB)
                .WaitFor(postgresDB);
        }

        break;
    default:
        throw new Exception("Please set the ASPIRE_DATABASE environment variable to either 'mssql' or 'postgresql'.");
}

builder.Build().Run();
