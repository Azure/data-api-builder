#:sdk Aspire.AppHost.Sdk@13.0.0
#:package Aspire.Hosting.SqlServer@13.0.0
#:package CommunityToolkit.Aspire.Hosting.SqlDatabaseProjects@9.8.1-beta.420
#:package CommunityToolkit.Aspire.Hosting.Azure.DataApiBuilder@9.8.1-beta.420
#:package CommunityToolkit.Aspire.Hosting.McpInspector@9.8.0

var builder = DistributedApplication.CreateBuilder(args);

// parameters
var sqlPath = new FileInfo("database.sql");
var sqlScript = File.ReadAllText(sqlPath.FullName);
var sqlPassword = builder.AddParameter("sql-password", "P@ssw0rd!");

// SQL Server
var sqlDatabase = builder
    .AddSqlServer("sql", sqlPassword)
    .WithDataVolume("sql-data")
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("StarTrek")
    .WithCreationScript(sqlScript);

var dabConfig = new FileInfo("dab-config.json");

// Data API builder
var dataApiBuilder = builder
    .AddContainer("dab", image: "azure-databases/data-api-builder", tag: "1.7.81-rc")
    .WithImageRegistry("mcr.microsoft.com")
    .WithBindMount(source: dabConfig.FullName, target: "/App/dab-config.json", isReadOnly: true)
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

// SQL Commander
var sqlCommander = builder
    .AddContainer("sql-cmdr", "jerrynixon/sql-commander", "latest")
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

// MCP Inspector
var mcpInspector =builder
    .AddMcpInspector("mcp")
    .WithMcpServer(dataApiBuilder)
    .WithParentRelationship(dataApiBuilder)
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    .WaitFor(dataApiBuilder)
    .WithUrls(context =>
    {
        context.Urls.First().DisplayText = "Inspector";
    });

await builder.Build().RunAsync();
