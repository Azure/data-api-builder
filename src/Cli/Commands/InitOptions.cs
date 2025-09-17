// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Product;
using Cli.Constants;
using CommandLine;
using Microsoft.Extensions.Logging;
using static Cli.Utils;

namespace Cli.Commands
{
    /// <summary>
    /// Init command options
    /// </summary>
    [Verb("init", isDefault: false, HelpText = "Initialize configuration file.", Hidden = false)]
    public class InitOptions : Options
    {
        public InitOptions(
            DatabaseType databaseType,
            string? connectionString,
            string? cosmosNoSqlDatabase,
            string? cosmosNoSqlContainer,
            string? graphQLSchemaPath,
            bool setSessionContext,
            HostMode hostMode,
            IEnumerable<string>? corsOrigin,
            string authenticationProvider,
            string? audience = null,
            string? issuer = null,
            string restPath = RestRuntimeOptions.DEFAULT_PATH,
            string? runtimeBaseRoute = null,
            bool restDisabled = false,
            string graphQLPath = GraphQLRuntimeOptions.DEFAULT_PATH,
            bool graphqlDisabled = false,
            string mcpPath = McpRuntimeOptions.DEFAULT_PATH,
            bool mcpDisabled = false,
            CliBool restEnabled = CliBool.None,
            CliBool graphqlEnabled = CliBool.None,
            CliBool mcpEnabled = CliBool.None,
            CliBool restRequestBodyStrict = CliBool.None,
            CliBool multipleCreateOperationEnabled = CliBool.None,
            string? config = null)
            : base(config)
        {
            DatabaseType = databaseType;
            ConnectionString = connectionString;
            CosmosNoSqlDatabase = cosmosNoSqlDatabase;
            CosmosNoSqlContainer = cosmosNoSqlContainer;
            GraphQLSchemaPath = graphQLSchemaPath;
            SetSessionContext = setSessionContext;
            HostMode = hostMode;
            CorsOrigin = corsOrigin;
            AuthenticationProvider = authenticationProvider;
            Audience = audience;
            Issuer = issuer;
            RestPath = restPath;
            RuntimeBaseRoute = runtimeBaseRoute;
            RestDisabled = restDisabled;
            GraphQLPath = graphQLPath;
            GraphQLDisabled = graphqlDisabled;
            McpPath = mcpPath;
            McpDisabled = mcpDisabled;
            RestEnabled = restEnabled;
            GraphQLEnabled = graphqlEnabled;
            McpEnabled = mcpEnabled;
            RestRequestBodyStrict = restRequestBodyStrict;
            MultipleCreateOperationEnabled = multipleCreateOperationEnabled;
        }

        /// <summary>
        /// Default constructor for InitOptions.
        /// Initializes non-nullable properties with their default values.
        /// </summary>
        public InitOptions() : base(null)
        {
            AuthenticationProvider = "StaticWebApps";
            RestPath = RestRuntimeOptions.DEFAULT_PATH;
            McpPath = McpRuntimeOptions.DEFAULT_PATH;
            GraphQLPath = GraphQLRuntimeOptions.DEFAULT_PATH;
        }

        [Option("database-type", Required = true, HelpText = "Type of database to connect. Supported values: mssql, cosmosdb_nosql, cosmosdb_postgresql, mysql, postgresql, dwsql")]
        public DatabaseType DatabaseType { get; set; }

        [Option("connection-string", Required = false, HelpText = "(Default: '') Connection details to connect to the database.")]
        public string? ConnectionString { get; set; }

        [Option("cosmosdb_nosql-database", Required = false, HelpText = "Database name for Azure Cosmos DB for NoSql.")]
        public string? CosmosNoSqlDatabase { get; set; }

        [Option("cosmosdb_nosql-container", Required = false, HelpText = "Container name for Azure Cosmos DB for NoSql.")]
        public string? CosmosNoSqlContainer { get; set; }

        [Option("graphql-schema", Required = false, HelpText = "GraphQL schema Path.")]
        public string? GraphQLSchemaPath { get; set; }

        [Option("set-session-context", Default = false, Required = false, HelpText = "Enable sending data to MsSql using session context.")]
        public bool SetSessionContext { get; set; }

        [Option("host-mode", Default = HostMode.Production, Required = false, HelpText = "Specify the Host mode - Development or Production")]
        public HostMode HostMode { get; set; }

        [Option("cors-origin", Separator = ',', Required = false, HelpText = "Specify the list of allowed origins.")]
        public IEnumerable<string>? CorsOrigin { get; set; }

        [Option("auth.provider", Default = "StaticWebApps", Required = false, HelpText = "Specify the Identity Provider.")]
        public string AuthenticationProvider { get; set; }

        [Option("auth.audience", Required = false, HelpText = "Identifies the recipients that the JWT is intended for.")]
        public string? Audience { get; set; }

        [Option("auth.issuer", Required = false, HelpText = "Specify the party that issued the jwt token.")]
        public string? Issuer { get; set; }

        [Option("rest.path", Default = RestRuntimeOptions.DEFAULT_PATH, Required = false, HelpText = "Specify the REST endpoint's default prefix.")]
        public string RestPath { get; }

        [Option("runtime.base-route", Default = null, Required = false, HelpText = "Specifies the base route for API requests.")]
        public string? RuntimeBaseRoute { get; set; }

        [Option("rest.disabled", Default = false, Required = false, HelpText = "Disables REST endpoint for all entities.")]
        public bool RestDisabled { get; set; }

        [Option("graphql.path", Default = GraphQLRuntimeOptions.DEFAULT_PATH, Required = false, HelpText = "Specify the GraphQL endpoint's default prefix.")]
        public string GraphQLPath { get; set; }

        [Option("graphql.disabled", Default = false, Required = false, HelpText = "Disables GraphQL endpoint for all entities.")]
        public bool GraphQLDisabled { get; }

        [Option("mcp.path", Default = McpRuntimeOptions.DEFAULT_PATH, Required = false, HelpText = "Specify the MCP endpoint's default prefix.")]
        public string McpPath { get; }

        [Option("mcp.disabled", Default = false, Required = false, HelpText = "Disables MCP endpoint for all entities.")]
        public bool McpDisabled { get; set; }

        [Option("rest.enabled", Required = false, HelpText = "(Default: true) Enables REST endpoint for all entities. Supported values: true, false.")]
        public CliBool RestEnabled { get; set; }

        [Option("graphql.enabled", Required = false, HelpText = "(Default: true) Enables GraphQL endpoint for all entities. Supported values: true, false.")]
        public CliBool GraphQLEnabled { get; set; }

        [Option("mcp.enabled", Required = false, HelpText = "(Default: true) Enables MCP endpoint for all entities. Supported values: true, false.")]
        public CliBool McpEnabled { get; set; }

        // Since the rest.request-body-strict option does not have a default value, it is required to specify a value for this option if it is
        // included in the init command.
        [Option("rest.request-body-strict", Required = false, HelpText = "(Default: true) Allow extraneous fields in the request body for REST.")]
        public CliBool RestRequestBodyStrict { get; set; }

        [Option("graphql.multiple-create.enabled", Required = false, HelpText = "(Default: false) Enables multiple create operation for GraphQL. Supported values: true, false.")]
        public CliBool MultipleCreateOperationEnabled { get; set; }
 
        [Option('c', "config", Required = false, HelpText = "Path to config file. Defaults to 'dab-config.json' unless 'dab-config.<DAB_ENVIRONMENT>.json' exists, where DAB_ENVIRONMENT is an environment variable.")]
        public new string? Config { get; set; }

        public int Handler(ILogger logger, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            logger.LogInformation("{productName} {version}", PRODUCT_NAME, ProductInfo.GetProductVersion());
            bool isSuccess = ConfigGenerator.TryGenerateConfig(this, loader, fileSystem);
            if (isSuccess)
            {
                logger.LogInformation("Config file generated.");
                logger.LogInformation("SUGGESTION: Use 'dab add [entity-name] [options]' to add new entities in your config.");
                return CliReturnCode.SUCCESS;
            }
            else
            {
                logger.LogError("Could not generate config file.");
                return CliReturnCode.GENERAL_ERROR;
            }
        }
    }
}
