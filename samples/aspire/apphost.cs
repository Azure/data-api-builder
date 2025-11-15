#:sdk Aspire.AppHost.Sdk@13.0.0
#:package Aspire.Hosting.SqlServer@13.0.0
#:package CommunityToolkit.Aspire.Hosting.SqlDatabaseProjects@9.8.1-beta.420
#:package CommunityToolkit.Aspire.Hosting.Azure.DataApiBuilder@9.8.1-beta.420
#:package CommunityToolkit.Aspire.Hosting.McpInspector@9.8.0

var builder = DistributedApplication.CreateBuilder(args);

var options = new
{
    SqlScript = "database.sql",
    SqlPassword = "P@ssw0rd!",
    SqlDatabase = "StarTrek",
    DabConfig = "dab-config.json",
    DabImage = "1.7.81-rc",
    SqlCmdrImage = "latest"
};

var sqlServer = builder
    .AddSqlServer("sql-server", builder.AddParameter("sql-password", options.SqlPassword))
    .WithDataVolume("sql-data-volume")
    .WithLifetime(ContainerLifetime.Persistent);

var sqlDatabase = sqlServer
    .AddDatabase("sql-database", options.SqlDatabase)
    .WithCreationScript(File.ReadAllText(new FileInfo(options.SqlScript).FullName));

var dabServer = builder
    .AddContainer("data-api", image: "azure-databases/data-api-builder", tag: options.DabImage)
    .WithImageRegistry("mcr.microsoft.com")
    .WithBindMount(source: new FileInfo(options.DabConfig).FullName, target: "/App/dab-config.json", isReadOnly: true)
    .WithHttpEndpoint(targetPort: 5000, name: "http")
    .WithEnvironment("MSSQL_CONNECTION_STRING", sqlDatabase)
    .WithUrls(context =>
    {
        context.Urls.Clear();
        context.Urls.Add(new() { Url = "/graphql", DisplayText = "Nitro", Endpoint = context.GetEndpoint("http") });
        context.Urls.Add(new() { Url = "/swagger", DisplayText = "Swagger", Endpoint = context.GetEndpoint("http") });
        context.Urls.Add(new() { Url = "/health", DisplayText = "Health", Endpoint = context.GetEndpoint("http") });
    })
    .WithOtlpExporter()
    .WithParentRelationship(sqlDatabase)
    .WithHttpHealthCheck("/health")
    .WaitFor(sqlDatabase);

var sqlCommander = builder
    .AddContainer("sql-cmdr", "jerrynixon/sql-commander", options.SqlCmdrImage)
    .WithImageRegistry("docker.io")
    .WithHttpEndpoint(targetPort: 8080, name: "http")
    .WithEnvironment("ConnectionStrings__db", sqlDatabase)
    .WithUrls(context =>
    {
        context.Urls.Clear();
        context.Urls.Add(new() { Url = "/", DisplayText = "Commander", Endpoint = context.GetEndpoint("http") });
    })
    .WithParentRelationship(sqlDatabase)
    .WithHttpHealthCheck("/health")
    .WaitFor(sqlDatabase);

var mcpInspector = builder
    .AddMcpInspector("mcp-inspector")
    .WithMcpServer(dabServer)
    .WithParentRelationship(dabServer)
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WaitFor(dabServer)
    .WithUrls(context =>
    {
        context.Urls.First().DisplayText = "Inspector";
    });

await builder.Build().RunAsync();