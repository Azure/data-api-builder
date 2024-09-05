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
            CliBool restEnabled = CliBool.None,
            CliBool graphqlEnabled = CliBool.None,
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
            RestEnabled = restEnabled;
            GraphQLEnabled = graphqlEnabled;
            RestRequestBodyStrict = restRequestBodyStrict;
            MultipleCreateOperationEnabled = multipleCreateOperationEnabled;
        }

        [Option("database-type", Required = true, HelpText = "Type of database to connect. Supported values: mssql, cosmosdb_nosql, cosmosdb_postgresql, mysql, postgresql, dwsql")]
        public DatabaseType DatabaseType { get; }

        [Option("connection-string", Required = false, HelpText = "(Default: '') Connection details to connect to the database.")]
        public string? ConnectionString { get; }

        [Option("cosmosdb_nosql-database", Required = false, HelpText = "Database name for Azure Cosmos DB for NoSql.")]
        public string? CosmosNoSqlDatabase { get; }

        [Option("cosmosdb_nosql-container", Required = false, HelpText = "Container name for Azure Cosmos DB for NoSql.")]
        public string? CosmosNoSqlContainer { get; }

        [Option("graphql-schema", Required = false, HelpText = "GraphQL schema Path.")]
        public string? GraphQLSchemaPath { get; }

        [Option("set-session-context", Default = false, Required = false, HelpText = "Enable sending data to MsSql using session context.")]
        public bool SetSessionContext { get; }

        [Option("host-mode", Default = HostMode.Production, Required = false, HelpText = "Specify the Host mode - Development or Production")]
        public HostMode HostMode { get; }

        [Option("cors-origin", Separator = ',', Required = false, HelpText = "Specify the list of allowed origins.")]
        public IEnumerable<string>? CorsOrigin { get; }

        [Option("auth.provider", Default = "StaticWebApps", Required = false, HelpText = "Specify the Identity Provider.")]
        public string AuthenticationProvider { get; }

        [Option("auth.audience", Required = false, HelpText = "Identifies the recipients that the JWT is intended for.")]
        public string? Audience { get; }

        [Option("auth.issuer", Required = false, HelpText = "Specify the party that issued the jwt token.")]
        public string? Issuer { get; }

        [Option("rest.path", Default = RestRuntimeOptions.DEFAULT_PATH, Required = false, HelpText = "Specify the REST endpoint's default prefix.")]
        public string RestPath { get; }

        [Option("runtime.base-route", Default = null, Required = false, HelpText = "Specifies the base route for API requests.")]
        public string? RuntimeBaseRoute { get; }

        [Option("rest.disabled", Default = false, Required = false, HelpText = "Disables REST endpoint for all entities.")]
        public bool RestDisabled { get; }

        [Option("graphql.path", Default = GraphQLRuntimeOptions.DEFAULT_PATH, Required = false, HelpText = "Specify the GraphQL endpoint's default prefix.")]
        public string GraphQLPath { get; }

        [Option("graphql.disabled", Default = false, Required = false, HelpText = "Disables GraphQL endpoint for all entities.")]
        public bool GraphQLDisabled { get; }

        [Option("rest.enabled", Required = false, HelpText = "(Default: true) Enables REST endpoint for all entities. Supported values: true, false.")]
        public CliBool RestEnabled { get; }

        [Option("graphql.enabled", Required = false, HelpText = "(Default: true) Enables GraphQL endpoint for all entities. Supported values: true, false.")]
        public CliBool GraphQLEnabled { get; }

        // Since the rest.request-body-strict option does not have a default value, it is required to specify a value for this option if it is
        // included in the init command.
        [Option("rest.request-body-strict", Required = false, HelpText = "(Default: true) Allow extraneous fields in the request body for REST.")]
        public CliBool RestRequestBodyStrict { get; }

        [Option("graphql.multiple-create.enabled", Required = false, HelpText = "(Default: false) Enables multiple create operation for GraphQL. Supported values: true, false.")]
        public CliBool MultipleCreateOperationEnabled { get; }

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
