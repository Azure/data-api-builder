var builder = DistributedApplication.CreateBuilder(args);

var sqlDbContainer = builder.AddSqlServer("sqlserver")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var sqlScript = File.ReadAllText("./init-scripts/create-database.sql");

var msSql = sqlDbContainer.AddDatabase("msSqlDb", "Trek")
    .WithCreationScript(sqlScript);

var pgScript = File.ReadAllText("./init-scripts/pg/create-database-pg.sql");

var postgresDB = builder.AddPostgres("postgres")
    //.WithDataVolume()
    .WithPgAdmin()
    .AddDatabase("pgDb", "postgres")
    .WithCreationScript(pgScript);

var mssqlService = builder.AddProject<Projects.Azure_DataApiBuilder_Service>("mssql-service", "Development")
    .WithArgs("-f", "net8.0")
    .WithEndpoint(endpointName: "https", (e) => e.Port = 6834)
    .WithEndpoint(endpointName: "http", (e) => e.Port = 8834)
    .WithEnvironment("ConnectionStrings__Trek", msSql)
    .WithEnvironment("db-type", "mssql")
    .WithUrls((e) =>
    {
        e.Urls.Clear();
        e.Urls.Add(new() { Url = "/swagger", DisplayText = "🔒Swagger", Endpoint = e.GetEndpoint("https") });
        e.Urls.Add(new() { Url = "/graphql", DisplayText = "🔒GraphQL", Endpoint = e.GetEndpoint("https") });
    })
    .WithHttpHealthCheck("/health")
    .WaitFor(msSql)
    .WithExplicitStart();

var pgService = builder.AddProject<Projects.Azure_DataApiBuilder_Service>("pg-service", "Development")
    .WithArgs("-f", "net8.0")
    .WithEndpoint(endpointName: "https", (e) => e.Port = 7834)
    .WithEndpoint(endpointName: "http", (e) => e.Port = 5834)
    .WithEnvironment("ConnectionStrings__Trek", postgresDB)
    .WithEnvironment("db-type", "postgresql")
    .WithUrls((e) =>
    {
        e.Urls.Clear();
        e.Urls.Add(new() { Url = "/swagger", DisplayText = "🔒Swagger", Endpoint = e.GetEndpoint("https") });
        e.Urls.Add(new() { Url = "/graphql", DisplayText = "🔒GraphQL", Endpoint = e.GetEndpoint("https") });
    })
    .WithHttpHealthCheck("/health")
    .WaitFor(postgresDB)
    .WithExplicitStart();

// BUG: Blocked due to https://github.com/dotnet/aspire/issues/10680
// msSql.WithParentRelationship(mssqlService);

// YOU NEED TO HAVE A SPACE HERE OR ELSE THIS DOESN'T WORK, WHAT EVEN IF THIS, PYTHON!?!?!
// postgresDB.WithParentRelationship(pgService);

builder.Build().Run();
