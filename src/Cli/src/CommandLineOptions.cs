using Azure.DataApiBuilder.Config;
using CommandLine;

namespace Cli
{
    /// <summary>
    /// Common options for all the commands
    /// </summary>
    public class Options
    {
        public Options(string config)
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
            string? cosmosDatabase,
            string? cosmosContainer,
            string? graphQLSchemaPath,
            HostModeType hostMode,
            IEnumerable<string>? corsOrigin,
            string config,
            string? devModeDefaultAuth)
            : base(config)
        {
            DatabaseType = databaseType;
            ConnectionString = connectionString;
            CosmosDatabase = cosmosDatabase;
            CosmosContainer = cosmosContainer;
            GraphQLSchemaPath = graphQLSchemaPath;
            HostMode = hostMode;
            CorsOrigin = corsOrigin;
            DevModeDefaultAuth = devModeDefaultAuth;
        }

        [Option("database-type", Required = true, HelpText = "Type of database to connect. Supported values: mssql, cosmos, mysql, postgresql")]
        public DatabaseType DatabaseType { get; }

        [Option("connection-string", Required = false, HelpText = "(Default: '') Connection details to connect to the database.")]
        public string? ConnectionString { get; }

        [Option("cosmos-database", Required = false, HelpText = "Database name for Cosmos DB.")]
        public string? CosmosDatabase { get; }

        [Option("cosmos-container", Required = false, HelpText = "Container name for Cosmos DB.")]
        public string? CosmosContainer { get; }

        [Option("graphql-schema", Required = false, HelpText = "GraphQL schema Path.")]
        public string? GraphQLSchemaPath { get; }

        [Option("host-mode", Default = HostModeType.Production, Required = false, HelpText = "Specify the Host mode - Development or Production")]
        public HostModeType HostMode { get; }

        [Option("cors-origin", Separator = ',', Required = false, HelpText = "Specify the list of allowed origins.")]
        public IEnumerable<string>? CorsOrigin { get; }

        [Option("authenticate-devmode-requests", Default = null, Required = false,
            HelpText = "boolean. Optional. Use when host-mode = Development. Treats all requests as authenticated in devmode when set to true.")]
        public string? DevModeDefaultAuth { get; }
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
            string? graphQLType,
            IEnumerable<string>? fieldsToInclude,
            IEnumerable<string>? fieldsToExclude,
            string? policyRequest,
            string? policyDatabase,
            string config)
            : base(config)
        {
            Entity = entity;
            SourceType = sourceType;
            SourceParameters = sourceParameters;
            SourceKeyFields = sourceKeyFields;
            RestRoute = restRoute;
            GraphQLType = graphQLType;
            FieldsToInclude = fieldsToInclude;
            FieldsToExclude = fieldsToExclude;
            PolicyRequest = policyRequest;
            PolicyDatabase = policyDatabase;
        }

        [Value(0, MetaName = "Entity", Required = true, HelpText = "Name of the entity.")]
        public string Entity { get; }

        [Option("source.type", Required = false, HelpText = "Type of the database object.Must be one of: [table, view, stored-procedure]")]
        public string? SourceType { get; }

        [Option("source.params", Required = false, Separator = ',', HelpText = "Dictionary of parameters and their values for Source object.")]
        public IEnumerable<string>? SourceParameters { get; }

        [Option("source.key-fields", Required = false, Separator = ',', HelpText = "The field(s) to be used as primary keys.")]
        public IEnumerable<string>? SourceKeyFields { get; }

        [Option("rest", Required = false, HelpText = "Route for rest api.")]
        public string? RestRoute { get; }

        [Option("graphql", Required = false, HelpText = "Type of graphQL.")]
        public string? GraphQLType { get; }

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
            string? graphQLType,
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
                  graphQLType,
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
            string? graphQLType,
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
                  graphQLType,
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
        public StartOptions(string config)
            : base(config) { }
    }
}
