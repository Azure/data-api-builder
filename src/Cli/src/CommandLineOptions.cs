using Azure.DataApiBuilder.Config;
using CommandLine;
using Microsoft.Extensions.Logging;

namespace Cli
{
    /// <summary>
    /// Common options for all the commands
    /// </summary>
    public class Options
    {
        public Options(string? config)
        {
            Config = config;
        }

        [Option('c', "config", Required = false, HelpText = "Path to config file. " +
            "Defaults to 'dab-config.json' unless 'dab-config.<DAB_ENVIRONMENT>.json' exists," +
            " where DAB_ENVIRONMENT is an environment variable.")]
        public string? Config { get; }
    }

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
            HostModeType hostMode,
            IEnumerable<string>? corsOrigin,
            string authenticationProvider,
            string? audience = null,
            string? issuer = null,
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
        }

        [Option("database-type", Required = true, HelpText = "Type of database to connect. Supported values: mssql, cosmosdb_nosql, cosmosdb_postgresql, mysql, postgresql")]
        public DatabaseType DatabaseType { get; }

        [Option("connection-string", Required = false, HelpText = "(Default: '') Connection details to connect to the database.")]
        public string? ConnectionString { get; }

        [Option("cosmosdb_nosql-database", Required = false, HelpText = "Database name for Cosmos DB for NoSql.")]
        public string? CosmosNoSqlDatabase { get; }

        [Option("cosmosdb_nosql-container", Required = false, HelpText = "Container name for Cosmos DB for NoSql.")]
        public string? CosmosNoSqlContainer { get; }

        [Option("graphql-schema", Required = false, HelpText = "GraphQL schema Path.")]
        public string? GraphQLSchemaPath { get; }

        [Option("set-session-context", Default = false, Required = false, HelpText = "Enable sending data to MsSql using session context.")]
        public bool SetSessionContext { get; }

        [Option("host-mode", Default = HostModeType.Production, Required = false, HelpText = "Specify the Host mode - Development or Production")]
        public HostModeType HostMode { get; }

        [Option("cors-origin", Separator = ',', Required = false, HelpText = "Specify the list of allowed origins.")]
        public IEnumerable<string>? CorsOrigin { get; }

        [Option("auth.provider", Default = "StaticWebApps", Required = false, HelpText = "Specify the Identity Provider.")]
        public string AuthenticationProvider { get; }

        [Option("auth.audience", Required = false, HelpText = "Identifies the recipients that the JWT is intended for.")]
        public string? Audience { get; }

        [Option("auth.issuer", Required = false, HelpText = "Specify the party that issued the jwt token.")]
        public string? Issuer { get; }
    }

    /// <summary>
    /// Command options for entity manipulation.
    /// </summary>
    public class EntityOptions : Options
    {
        public EntityOptions(
            string entity,
            string? sourceType,
            IEnumerable<string>? sourceParameters,
            IEnumerable<string>? sourceKeyFields,
            string? restRoute,
            IEnumerable<string>? restMethodsForStoredProcedure,
            string? graphQLType,
            string? graphQLOperationForStoredProcedure,
            IEnumerable<string>? fieldsToInclude,
            IEnumerable<string>? fieldsToExclude,
            string? policyRequest,
            string? policyDatabase,
            string? config)
            : base(config)
        {
            Entity = entity;
            SourceType = sourceType;
            SourceParameters = sourceParameters;
            SourceKeyFields = sourceKeyFields;
            RestRoute = restRoute;
            RestMethodsForStoredProcedure = restMethodsForStoredProcedure;
            GraphQLType = graphQLType;
            GraphQLOperationForStoredProcedure = graphQLOperationForStoredProcedure;
            FieldsToInclude = fieldsToInclude;
            FieldsToExclude = fieldsToExclude;
            PolicyRequest = policyRequest;
            PolicyDatabase = policyDatabase;
        }

        // Entity is required but we have made required as false to have custom error message (more user friendly), if not provided.
        [Value(0, MetaName = "Entity", Required = false, HelpText = "Name of the entity.")]
        public string Entity { get; }

        [Option("source.type", Required = false, HelpText = "Type of the database object.Must be one of: [table, view, stored-procedure]")]
        public string? SourceType { get; }

        [Option("source.params", Required = false, Separator = ',', HelpText = "Dictionary of parameters and their values for Source object.\"param1:val1,param2:value2,..\"")]
        public IEnumerable<string>? SourceParameters { get; }

        [Option("source.key-fields", Required = false, Separator = ',', HelpText = "The field(s) to be used as primary keys.")]
        public IEnumerable<string>? SourceKeyFields { get; }

        [Option("rest", Required = false, HelpText = "Route for rest api.")]
        public string? RestRoute { get; }

        [Option("rest.methods", Required = false, Separator = ',',  HelpText = "HTTP actions to be supported for stored procedure. Specify the actions as a comma separated list.")]
        public IEnumerable<string>? RestMethodsForStoredProcedure { get; }

        [Option("graphql", Required = false, HelpText = "Type of graphQL.")]
        public string? GraphQLType { get; }

        [Option("graphql.operation", Required = false, HelpText = "GraphQL operation to be supported for stored procedure.")]
        public string? GraphQLOperationForStoredProcedure { get; }

        [Option("fields.include", Required = false, Separator = ',', HelpText = "Fields that are allowed access to permission.")]
        public IEnumerable<string>? FieldsToInclude { get; }

        [Option("fields.exclude", Required = false, Separator = ',', HelpText = "Fields that are excluded from the action lists.")]
        public IEnumerable<string>? FieldsToExclude { get; }

        [Option("policy-request", Required = false, HelpText = "Specify the rule to be checked before sending any request to the database.")]
        public string? PolicyRequest { get; }

        [Option("policy-database", Required = false, HelpText = "Specify an OData style filter rule that will be injected in the query sent to the database.")]
        public string? PolicyDatabase { get; }
    }

    /// <summary>
    /// Add command options
    /// </summary>
    [Verb("add", isDefault: false, HelpText = "Add a new entity to the configuration file.", Hidden = false)]
    public class AddOptions : EntityOptions
    {
        public AddOptions(
            string source,
            IEnumerable<string> permissions,
            string entity,
            string? sourceType,
            IEnumerable<string>? sourceParameters,
            IEnumerable<string>? sourceKeyFields,
            string? restRoute,
            IEnumerable<string>? restMethodsForStoredProcedure,
            string? graphQLType,
            string? graphQLOperationForStoredProcedure,
            IEnumerable<string>? fieldsToInclude,
            IEnumerable<string>? fieldsToExclude,
            string? policyRequest,
            string? policyDatabase,
            string? config)
            : base(entity,
                  sourceType,
                  sourceParameters,
                  sourceKeyFields,
                  restRoute,
                  restMethodsForStoredProcedure,
                  graphQLType,
                  graphQLOperationForStoredProcedure,
                  fieldsToInclude,
                  fieldsToExclude,
                  policyRequest,
                  policyDatabase,
                  config)
        {
            Source = source;
            Permissions = permissions;
        }

        [Option('s', "source", Required = true, HelpText = "Name of the source database object.")]
        public string Source { get; }

        [Option("permissions", Required = true, Separator = ':', HelpText = "Permissions required to access the source table or container.")]
        public IEnumerable<string> Permissions { get; }
    }

    /// <summary>
    /// Update command options
    /// </summary>
    [Verb("update", isDefault: false, HelpText = "Update an existing entity in the configuration file.", Hidden = false)]
    public class UpdateOptions : EntityOptions
    {
        public UpdateOptions(
            string? source,
            IEnumerable<string>? permissions,
            string? relationship,
            string? cardinality,
            string? targetEntity,
            string? linkingObject,
            IEnumerable<string>? linkingSourceFields,
            IEnumerable<string>? linkingTargetFields,
            IEnumerable<string>? relationshipFields,
            IEnumerable<string>? map,
            string entity,
            string? sourceType,
            IEnumerable<string>? sourceParameters,
            IEnumerable<string>? sourceKeyFields,
            string? restRoute,
            IEnumerable<string>? restMethodsForStoredProcedure,
            string? graphQLType,
            string? graphQLOperationForStoredProcedure,
            IEnumerable<string>? fieldsToInclude,
            IEnumerable<string>? fieldsToExclude,
            string? policyRequest,
            string? policyDatabase,
            string config)
            : base(entity,
                  sourceType,
                  sourceParameters,
                  sourceKeyFields,
                  restRoute,
                  restMethodsForStoredProcedure,
                  graphQLType,
                  graphQLOperationForStoredProcedure,
                  fieldsToInclude,
                  fieldsToExclude,
                  policyRequest,
                  policyDatabase,
                  config)
        {
            Source = source;
            Permissions = permissions;
            Relationship = relationship;
            Cardinality = cardinality;
            TargetEntity = targetEntity;
            LinkingObject = linkingObject;
            LinkingSourceFields = linkingSourceFields;
            LinkingTargetFields = linkingTargetFields;
            RelationshipFields = relationshipFields;
            Map = map;
        }

        [Option('s', "source", Required = false, HelpText = "Name of the source table or container.")]
        public string? Source { get; }

        [Option("permissions", Required = false, Separator = ':', HelpText = "Permissions required to access the source table or container.")]
        public IEnumerable<string>? Permissions { get; }

        [Option("relationship", Required = false, HelpText = "Specify relationship between two entities.")]
        public string? Relationship { get; }

        [Option("cardinality", Required = false, HelpText = "Specify cardinality between two entities.")]
        public string? Cardinality { get; }

        [Option("target.entity", Required = false, HelpText = "Another exposed entity to which the source entity relates to.")]
        public string? TargetEntity { get; }

        [Option("linking.object", Required = false, HelpText = "Database object that is used to support an M:N relationship.")]
        public string? LinkingObject { get; }

        [Option("linking.source.fields", Required = false, Separator = ',', HelpText = "Database fields in the linking object to connect to the related item in the source entity.")]
        public IEnumerable<string>? LinkingSourceFields { get; }

        [Option("linking.target.fields", Required = false, Separator = ',', HelpText = "Database fields in the linking object to connect to the related item in the target entity.")]
        public IEnumerable<string>? LinkingTargetFields { get; }

        [Option("relationship.fields", Required = false, Separator = ':', HelpText = "Specify fields to be used for mapping the entities.")]
        public IEnumerable<string>? RelationshipFields { get; }

        [Option('m', "map", Separator = ',', Required = false, HelpText = "Specify mappings between database fields and GraphQL and REST fields. format: --map \"backendName1:exposedName1,backendName2:exposedName2,...\".")]
        public IEnumerable<string>? Map { get; }
    }

    /// <summary>
    /// Start command options
    /// </summary>
    [Verb("start", isDefault: false, HelpText = "Start Data Api Builder Engine", Hidden = false)]
    public class StartOptions : Options
    {
        public StartOptions(bool verbose, LogLevel? logLevel, string config)
            : base(config)
        {
            // When verbose is true we set LogLevel to information.
            LogLevel = verbose is true ? Microsoft.Extensions.Logging.LogLevel.Information : logLevel;
        }

        // SetName defines mutually exclusive sets, ie: can not have
        // both verbose and LogLevel.
        [Option("verbose", SetName = "verbose", Required = false, HelpText = "Specify logging level as informational.")]
        public bool Verbose { get; }
        [Option("LogLevel", SetName = "LogLevel", Required = false, HelpText = "Specify logging level as provided value, " +
            "see: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.loglevel?view=dotnet-plat-ext-7.0")]
        public LogLevel? LogLevel { get; }
    }
}
