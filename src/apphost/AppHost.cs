var builder = DistributedApplication.CreateBuilder(args);

var sqlDbContainer = builder.AddSqlServer("sqlserver")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var sqlScript = File.ReadAllText("./init-scripts/create-database.sql");

var msSql = sqlDbContainer.AddDatabase("msSqlDb", "Trek")
    .WithCreationScript(sqlScript);

var postgresDB = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin()
    .WithInitFiles("./init-scripts/pg")
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("pgSqlDb", "Trek");

builder.AddProject<Projects.Azure_DataApiBuilder_Service>("mssql-service", "Development")
    .WithArgs("-f", "net8.0")
    .WithEndpoint(endpointName: "https", (e) =>
    {
        e.Port = 6834;
    })
    .WithEnvironment("ConnectionStrings__Trek", msSql)
    .WithEnvironment("db-type", "mssql")
    .WithUrls((e) =>
    {
        e.Urls.Clear();
        e.Urls.Add(new() { Url = "/swagger", DisplayText = "🔒Swagger", Endpoint = e.GetEndpoint("https") });
        e.Urls.Add(new() { Url = "/graphql", DisplayText = "🔒GraphQL", Endpoint = e.GetEndpoint("https") });
    })
    .WithHttpHealthCheck("/health")
    .WaitFor(msSql);

builder.AddProject<Projects.Azure_DataApiBuilder_Service>("pg-service", "Development")
    .WithArgs("-f", "net8.0")
    .WithEndpoint(endpointName: "https", (e) =>
    {
        e.Port = 7834;
    })
    .WithEnvironment("ConnectionStrings__Trek", postgresDB)
    .WithEnvironment("db-type", "postgresql")
    .WithUrls((e) =>
    {
        e.Urls.Clear();
        e.Urls.Add(new() { Url = "/swagger", DisplayText = "🔒Swagger", Endpoint = e.GetEndpoint("https") });
        e.Urls.Add(new() { Url = "/graphql", DisplayText = "🔒GraphQL", Endpoint = e.GetEndpoint("https") });
    })
    .WithHttpHealthCheck("/health")
    .WaitFor(postgresDB);

builder.Build().Run();
