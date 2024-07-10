// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.Net;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Configurations;

/// <summary>
/// This class encapsulates methods to validate the runtime config file.
/// </summary>
public class RuntimeConfigValidator : IConfigValidator
{
    private readonly RuntimeConfigProvider _runtimeConfigProvider;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<RuntimeConfigValidator> _logger;

    // Only characters from a-z,A-Z,0-9,.,_ are allowed to be present within the claimType.
    private static readonly string _invalidClaimChars = @"[^a-zA-Z0-9_\.]+";

    private bool _isValidateOnly;
    public List<Exception> ConfigValidationExceptions { get; private set; }

    // Regex to check occurrence of any character not among [a-z,A-Z,0-9,.,_] in the claimType.
    // The claimType is invalid if there is a match found.
    private static readonly Regex _invalidClaimCharsRgx = new(_invalidClaimChars, RegexOptions.Compiled);

    // Reserved characters as defined in RFC3986 are not allowed to be present in the
    // REST/GraphQL custom path because they are not acceptable to be present in URIs.
    // Refer here: https://www.rfc-editor.org/rfc/rfc3986#page-12.
    private static readonly string _reservedUriChars = @"[\.:\?#/\[\]@!$&'()\*\+,;=]+";

    //  Regex to validate rest/graphql custom path prefix.
    public static readonly Regex _reservedUriCharsRgx = new(_reservedUriChars, RegexOptions.Compiled);

    // Regex used to extract all claimTypes in policy. It finds all the substrings which are
    // of the form @claims.*** delimited by space character,end of the line or end of the string.
    private static readonly string _claimChars = @"@claims\.[^\s\)]*";

    // Error messages.
    public const string INVALID_CLAIMS_IN_POLICY_ERR_MSG = "One or more claim types supplied in the database policy are not supported.";
    public const string URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG = "contains one or more reserved characters.";

    public RuntimeConfigValidator(
        RuntimeConfigProvider runtimeConfigProvider,
        IFileSystem fileSystem,
        ILogger<RuntimeConfigValidator> logger,
        bool isValidateOnly = false)
    {
        _runtimeConfigProvider = runtimeConfigProvider;
        _fileSystem = fileSystem;
        _logger = logger;
        _isValidateOnly = isValidateOnly;
        ConfigValidationExceptions = new();
    }

    /// <summary>
    /// The driver for validation of the runtime configuration file.
    /// </summary>
    public void ValidateConfigProperties()
    {
        RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();

        ValidateDataSourceInConfig(
            runtimeConfig,
            _fileSystem,
            _logger);

        ValidateAuthenticationOptions(runtimeConfig);
        ValidateGlobalEndpointRouteConfig(runtimeConfig);
        //ValidateAppInsightsTelemetryConnectionString(runtimeConfig);

        // Running these graphQL validations only in development mode to ensure
        // fast startup of engine in production mode.
        if (runtimeConfig.IsDevelopmentMode())
        {
            ValidateEntityConfiguration(runtimeConfig);

            if (runtimeConfig.IsGraphQLEnabled)
            {
                ValidateEntitiesDoNotGenerateDuplicateQueriesOrMutation(runtimeConfig.DataSource.DatabaseType, runtimeConfig.Entities);
            }
        }
    }

    /// <summary>
    /// Throws exception if Invalid connection-string or database type
    /// is present in the config
    /// </summary>
    public void ValidateDataSourceInConfig(
        RuntimeConfig runtimeConfig,
        IFileSystem fileSystem,
        ILogger logger)
    {
        foreach (DataSource dataSource in runtimeConfig.ListAllDataSources())
        {
            // Connection string can't be null or empty
            if (string.IsNullOrWhiteSpace(dataSource.ConnectionString))
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization));
            }
        }

        ValidateDatabaseType(runtimeConfig, fileSystem, logger);
    }

    /// <summary>
    /// A connection string to send telemetry to Application Insights is required if telemetry is enabled.
    /// </summary>
    public void ValidateAppInsightsTelemetryConnectionString(RuntimeConfig runtimeConfig)
    {
        if (runtimeConfig.Runtime!.Telemetry is not null && runtimeConfig.Runtime.Telemetry.ApplicationInsights is not null)
        {
            ApplicationInsightsOptions applicationInsightsOptions = runtimeConfig.Runtime.Telemetry.ApplicationInsights;
            if (applicationInsightsOptions.Enabled && string.IsNullOrWhiteSpace(applicationInsightsOptions.ConnectionString))
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: "Application Insights connection string cannot be null or empty if enabled.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }
        }
    }

    /// <summary>
    /// This method runs several validations against the config file such as schema validation,
    /// validation of entities metadata, validation of permissions, validation of entity configuration.
    /// This method is called by the CLI when the user runs `validate` command with `isValidateOnly=true`.
    /// </summary>
    /// <param name="configFilePath">full/relative config file path with extension</param>
    /// <param name="loggerFactory">Logger Factory</param>
    /// <returns>true if no validation failures, else false.</returns>
    public async Task<bool> TryValidateConfig(
        string configFilePath,
        ILoggerFactory loggerFactory)
    {
        RuntimeConfig? runtimeConfig;

        if (!_runtimeConfigProvider.TryGetConfig(out runtimeConfig))
        {
            _logger.LogInformation("Failed to parse the config file");
            return false;
        }

        JsonSchemaValidationResult validationResult = await ValidateConfigSchema(runtimeConfig, configFilePath, loggerFactory);
        ValidateConfigProperties();
        ValidatePermissionsInConfig(runtimeConfig);

        _logger.LogInformation("Validating entity relationships.");
        ValidateRelationshipConfigCorrectness(runtimeConfig);
        await ValidateEntitiesMetadata(runtimeConfig, loggerFactory);

        if (validationResult.IsValid && !ConfigValidationExceptions.Any())
        {
            return true;
        }
        else
        {
            if (!validationResult.IsValid)
            {
                // log schema validation errors
                _logger.LogError(validationResult.ErrorMessage);
            }

            // log config validation errors
            LogConfigValidationExceptions();
            return false;
        }
    }

    /// <summary>
    /// This method runs schema validation against the config file.
    /// It uses runtime config object to check if the schema uri is provided in the config file
    /// </summary>
    public async Task<JsonSchemaValidationResult> ValidateConfigSchema(RuntimeConfig runtimeConfig, string configFilePath, ILoggerFactory loggerFactory)
    {
        string jsonData = runtimeConfig.ToJson();
        ILogger<JsonConfigSchemaValidator> jsonConfigValidatorLogger = loggerFactory.CreateLogger<JsonConfigSchemaValidator>();
        JsonConfigSchemaValidator jsonConfigSchemaValidator = new(jsonConfigValidatorLogger, _fileSystem);

        string? jsonSchema = await jsonConfigSchemaValidator.GetJsonSchema(runtimeConfig);

        if (string.IsNullOrWhiteSpace(jsonSchema))
        {
            _logger.LogError("Failed to get the json schema for the config.");
            return new JsonSchemaValidationResult(isValid: false, errors: null);
        }

        return await jsonConfigSchemaValidator.ValidateJsonConfigWithSchemaAsync(jsonSchema, jsonData);
    }

    /// <summary>
    /// Validates the semantic correctness of an Entity's relationships in the
    /// runtime configuration without cross referencing DB metadata.
    /// Validating Cases:
    /// 1. Entities not defined in the config cannot be used in a relationship.
    /// 2. Entities with graphQL disabled cannot be used in a relationship with another entity.
    /// </summary>
    /// <exception cref="DataApiBuilderException">Throws exception whenever some validation fails.</exception>
    public void ValidateRelationshipConfigCorrectness(RuntimeConfig runtimeConfig)
    {
        // Loop through each entity in the config and verify its relationship.
        foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
        {
            // Skipping relationship validation if entity has no relationship
            // or if graphQL is disabled.
            if (entity.Relationships is null || !entity.GraphQL.Enabled)
            {
                continue;
            }

            if (entity.Source.Type is not EntitySourceType.Table && entity.Relationships is not null
                && entity.Relationships.Count > 0)
            {
                HandleOrRecordException(new DataApiBuilderException(
                        message: $"Cannot define relationship for entity: {entityName}",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }

            string databaseName = runtimeConfig.GetDataSourceNameFromEntityName(entityName);

            foreach ((string relationshipName, EntityRelationship relationship) in entity.Relationships!)
            {
                // Validate if entity referenced in relationship is defined in the config.
                if (!runtimeConfig.Entities.TryGetValue(relationship.TargetEntity, out Entity? targetEntity))
                {
                    HandleOrRecordException(new DataApiBuilderException(
                        message: $"Entity: {relationship.TargetEntity} used for relationship is not defined in the config.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                }

                // Validation to ensure that an entity with graphQL disabled cannot be referenced in a relationship by other entities
                EntityGraphQLOptions? targetEntityGraphQLDetails = targetEntity is not null ? targetEntity.GraphQL : null;
                if (targetEntityGraphQLDetails is not null && !targetEntityGraphQLDetails.Enabled)
                {
                    HandleOrRecordException(new DataApiBuilderException(
                        message: $"Entity: {relationship.TargetEntity} is disabled for GraphQL.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                }

                // Linking object is null and therefore we have a many-one or a one-many relationship. These relationships
                // must be validated separately from many-many relationships. In one-many and many-one relationships, the count
                // of source and target fields need to match, or if one is null the other must be as well.
                // If both of these sets of fields are null, foreign key information, inferred from the database metadata,
                // will be used to define the relationship.
                // see: https://learn.microsoft.com/en-us/azure/data-api-builder/relationships
                if (string.IsNullOrWhiteSpace(relationship.LinkingObject))
                {
                    // Validation to ensure that source and target fields are both null or both not null.
                    if (relationship.SourceFields is null ^ relationship.TargetFields is null)
                    {
                        HandleOrRecordException(new DataApiBuilderException(
                            message: $"Entity: {entityName} has a relationship: {relationshipName}, which has source and target fields " +
                                $"where one is null and the other is not.",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                    }

                    // In one-one, one-many, or many-one relationships when both source and target are non null their size must match.
                    if ((relationship.SourceFields is not null && relationship.TargetFields is not null) &&
                        relationship.SourceFields.Length != relationship.TargetFields.Length)
                    {
                        HandleOrRecordException(new DataApiBuilderException(
                            message: $"Entity: {entityName} has a relationship: {relationshipName}, which has {relationship.SourceFields.Length} source fields defined, " +
                                $"but {relationship.TargetFields.Length} target fields defined.",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                    }
                }

                // Linking object exists and we therefore have a many-many relationship. Validation here differs from one-many and many-one in that
                // the source and target fields are now only indirectly related through the linking object. Therefore, it is the source and linkingSource
                // along with the target and linkingTarget fields that must match, respectively. Source and linkingSource fields provide the relationship
                // from the source entity to the linkingObject while target and linkingTarget fields provide the relationship from the target entity to the
                // linkingObject.
                // see: https://learn.microsoft.com/en-us/azure/data-api-builder/relationships#many-to-many-relationship
                if (!string.IsNullOrWhiteSpace(relationship.LinkingObject))
                {
                    ValidateFieldsAndAssociatedLinkingFields(
                        fields: relationship.SourceFields,
                        linkingFields: relationship.LinkingSourceFields,
                        fieldType: "source",
                        entityName: entityName,
                        relationshipName: relationshipName);
                    ValidateFieldsAndAssociatedLinkingFields(
                        fields: relationship.TargetFields,
                        linkingFields: relationship.LinkingTargetFields,
                        fieldType: "target",
                        entityName: entityName,
                        relationshipName: relationshipName);
                }
            }
        }
    }

    /// <summary>
    /// This method validates the entities relationships against the database objects using
    /// metadata from the backend DB generated by this function.
    /// </summary>
    public async Task ValidateEntitiesMetadata(RuntimeConfig runtimeConfig, ILoggerFactory loggerFactory)
    {
        QueryManagerFactory queryManagerFactory = new(
            runtimeConfigProvider: _runtimeConfigProvider,
            logger: loggerFactory.CreateLogger<IQueryExecutor>(),
            contextAccessor: null!);

        // create metadata provider factory to validate metadata against the database
        MetadataProviderFactory metadataProviderFactory = new(
            runtimeConfigProvider: _runtimeConfigProvider,
            queryManagerFactory: queryManagerFactory,
            logger: loggerFactory.CreateLogger<ISqlMetadataProvider>(),
            fileSystem: _fileSystem,
            isValidateOnly: _isValidateOnly);
        await metadataProviderFactory.InitializeAsync();

        ConfigValidationExceptions.AddRange(metadataProviderFactory.GetAllMetadataExceptions());

        // Validation below relies on metadata being populated from backend, only execute when there were no connection errors
        if (!ConfigValidationExceptions.Any(x => x.Message.StartsWith(DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE)))
        {
            ValidateRelationships(runtimeConfig, metadataProviderFactory);
        }
    }

    /// <summary>
    /// Helper method to log exceptions occured during validation of the config file.
    /// </summary>
    private void LogConfigValidationExceptions()
    {
        foreach (Exception exception in ConfigValidationExceptions)
        {
            _logger.LogError(exception.Message);
        }
    }

    /// <summary>
    /// Throws exception if database type is incorrectly configured
    /// in the config.
    /// </summary>
    public void ValidateDatabaseType(
        RuntimeConfig runtimeConfig,
        IFileSystem fileSystem,
        ILogger logger)
    {
        // Schema file should be present in the directory if not specified in the config
        // when using CosmosDB_NoSQL database.
        foreach (DataSource dataSource in runtimeConfig.ListAllDataSources())
        {
            if (dataSource.DatabaseType is DatabaseType.CosmosDB_NoSQL)
            {
                try
                {
                    CosmosDbNoSQLDataSourceOptions? cosmosDbNoSql =
                        dataSource.GetTypedOptions<CosmosDbNoSQLDataSourceOptions>() ??
                        throw new DataApiBuilderException(
                            "CosmosDB_NoSql is specified but no CosmosDB_NoSql configuration information has been provided.",
                            HttpStatusCode.ServiceUnavailable,
                            DataApiBuilderException.SubStatusCodes.ErrorInInitialization);

                    // The schema is provided through GraphQLSchema and not the Schema file when the configuration
                    // is received after startup.
                    if (string.IsNullOrEmpty(cosmosDbNoSql.GraphQLSchema))
                    {
                        if (string.IsNullOrEmpty(cosmosDbNoSql.Schema))
                        {
                            throw new DataApiBuilderException(
                                "No GraphQL schema file has been provided for CosmosDB_NoSql. Ensure you provide a GraphQL schema containing the GraphQL object types to expose.",
                                HttpStatusCode.ServiceUnavailable,
                                DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
                        }

                        if (!fileSystem.File.Exists(cosmosDbNoSql.Schema))
                        {
                            throw new FileNotFoundException($"The GraphQL schema file at '{cosmosDbNoSql.Schema}' could not be found. Ensure that it is a path relative to the runtime.");
                        }
                    }
                }
                catch (Exception e)
                {
                    HandleOrRecordException(e);
                }
            }
        }
    }

    /// <summary>
    /// Validate that the entities that have graphQL exposed do not generate queries with the
    /// same name.
    /// For example: Consider the entity definitions
    /// "Book": {
    ///   "graphql": true
    /// }
    ///
    /// "book": {
    ///     "graphql": true
    /// }
    /// "Notebook": {
    ///     "graphql": {
    ///         "type": {
    ///             "singular": "book",
    ///             "plural": "books"
    ///         }
    ///     }
    /// }
    /// All these entities will create queries with the following field names
    /// pk query name: book_by_pk
    /// List query name: books
    /// create mutation name: createBook
    /// update mutation name: updateBook
    /// delete mutation name: deleteBook
    /// patch mutation name: patchBook
    /// </summary>
    /// <param name="entityCollection">Entity definitions</param>
    /// <exception cref="DataApiBuilderException"></exception>
    public void ValidateEntitiesDoNotGenerateDuplicateQueriesOrMutation(DatabaseType databaseType, RuntimeEntities entityCollection)
    {
        HashSet<string> graphQLOperationNames = new();

        foreach ((string entityName, Entity entity) in entityCollection)
        {
            if (!entity.GraphQL.Enabled)
            {
                continue;
            }

            bool containsDuplicateOperationNames = false;
            if (entity.Source.Type is EntitySourceType.StoredProcedure)
            {
                // For Stored Procedures a single query/mutation is generated.
                string storedProcedureQueryName = GraphQLNaming.GenerateStoredProcedureGraphQLFieldName(entityName, entity);

                if (!graphQLOperationNames.Add(storedProcedureQueryName))
                {
                    containsDuplicateOperationNames = true;
                }
            }
            else
            {
                // For entities (table/view) that have graphQL exposed, two queries, three mutations for Relational databases (e.g. MYSQL, MSSQL etc.)
                // and four mutations for CosmosDb_NoSQL would be generated.
                // Primary Key Query: For fetching an item using its primary key.
                // List Query: To fetch a paginated list of items.
                // Query names for both these queries are determined.
                string pkQueryName = GraphQLNaming.GenerateByPKQueryName(entityName, entity);
                string listQueryName = GraphQLNaming.GenerateListQueryName(entityName, entity);

                // Mutations names for the exposed entities are determined.
                string createMutationName = $"create{GraphQLNaming.GetDefinedSingularName(entityName, entity)}";
                string updateMutationName = $"update{GraphQLNaming.GetDefinedSingularName(entityName, entity)}";
                string deleteMutationName = $"delete{GraphQLNaming.GetDefinedSingularName(entityName, entity)}";
                string patchMutationName = $"patch{GraphQLNaming.GetDefinedSingularName(entityName, entity)}";

                if (!graphQLOperationNames.Add(pkQueryName)
                    || !graphQLOperationNames.Add(listQueryName)
                    || !graphQLOperationNames.Add(createMutationName)
                    || !graphQLOperationNames.Add(updateMutationName)
                    || !graphQLOperationNames.Add(deleteMutationName)
                    || ((databaseType is DatabaseType.CosmosDB_NoSQL) && !graphQLOperationNames.Add(patchMutationName)))
                {
                    containsDuplicateOperationNames = true;
                }
            }

            if (containsDuplicateOperationNames)
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: $"Entity {entityName} generates queries/mutation that already exist",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }
        }
    }

    /// <summary>
    /// Check whether the entity configuration defined in runtime config only contains characters allowed for GraphQL names
    /// and other validations related to rest path and methods configured for the entity.
    /// The GraphQL validation is not performed for entities which do not
    /// have GraphQL configuration: when entity.GraphQL == false or null.
    /// </summary>
    /// <seealso cref="https://spec.graphql.org/October2021/#Name"/>
    /// <param name="runtimeConfig">The runtime configuration.</param>
    public void ValidateEntityConfiguration(RuntimeConfig runtimeConfig)
    {
        // Stores the unique rest paths configured for different entities present in the config.
        HashSet<string> restPathsForEntities = new();

        foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
        {
            if (runtimeConfig.IsRestEnabled && entity.Rest is not null && entity.Rest.Enabled)
            {
                // If no custom rest path is defined for the entity, we default it to the entityName.
                string pathForEntity = entity.Rest.Path is not null ? entity.Rest.Path.TrimStart('/') : entityName;
                try
                {
                    ValidateRestPathSettingsForEntity(entityName, pathForEntity);
                    if (!restPathsForEntities.Add(pathForEntity))
                    {
                        // Presence of multiple entities having the same rest path configured causes conflict.
                        throw new DataApiBuilderException(
                            message: $"The rest path: {pathForEntity} specified for entity: {entityName} is already used by another entity.",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError
                            );
                    }
                }
                catch (DataApiBuilderException e)
                {
                    HandleOrRecordException(e);
                }

                ValidateRestMethods(entity, entityName);
            }

            // If GraphQL endpoint is enabled globally and at entity level, then only we perform the validations related to it.
            if (runtimeConfig.IsGraphQLEnabled && entity.GraphQL is not null && entity.GraphQL.Enabled)
            {
                ValidateNameRequirements(entity.GraphQL.Singular);
                ValidateNameRequirements(entity.GraphQL.Plural);
            }
        }
    }

    /// <summary>
    /// Helper method to validate and let users know whether insignificant properties are present in the REST field.
    /// Currently, it checks for the presence of Methods property when the entity type is table/view and logs a warning.
    /// Methods property plays a role only in case of stored procedures.
    /// </summary>
    /// <param name="entity">Entity object for which validation is performed</param>
    /// <param name="entityName">Name of the entity</param>
    private void ValidateRestMethods(Entity entity, string entityName)
    {
        if (entity.Source.Type is not EntitySourceType.StoredProcedure && entity.Rest.Methods is not null && entity.Rest.Methods.Length > 0)
        {
            _logger.LogWarning("Entity {entityName} has rest methods configured but is not a stored procedure. Values configured will be ignored and all 5 HTTP actions will be enabled.", entityName);
        }
    }

    /// <summary>
    /// Helper method to validate that the rest path property for the entity is correctly configured.
    /// The rest path should not be null/empty and should not contain any reserved characters.
    /// </summary>
    /// <param name="entityName">Name of the entity.</param>
    /// <param name="pathForEntity">The rest path for the entity.</param>
    /// <exception cref="DataApiBuilderException">Throws exception when rest path contains an unexpected value.</exception>
    private static void ValidateRestPathSettingsForEntity(string entityName, string pathForEntity)
    {
        if (string.IsNullOrEmpty(pathForEntity))
        {
            // The rest 'path' cannot be empty.
            throw new DataApiBuilderException(
                message: $"The rest path for entity: {entityName} cannot be empty.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError
                );
        }

        if (_reservedUriCharsRgx.IsMatch(pathForEntity))
        {
            throw new DataApiBuilderException(
                message: $"The rest path: {pathForEntity} for entity: {entityName} contains one or more reserved characters.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError
                );
        }
    }

    private void ValidateNameRequirements(string entityName)
    {
        if (GraphQLNaming.ViolatesNamePrefixRequirements(entityName) ||
            GraphQLNaming.ViolatesNameRequirements(entityName))
        {
            HandleOrRecordException(new DataApiBuilderException(
                message: $"Entity {entityName} contains characters disallowed by GraphQL.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError)
            );
        }
    }

    /// <summary>
    /// Ensure the global REST and GraphQL endpoints do not conflict if both
    /// are enabled.
    /// </summary>
    /// <param name="runtimeConfig">The config that will be validated.</param>
    public void ValidateGlobalEndpointRouteConfig(RuntimeConfig runtimeConfig)
    {
        // Both REST and GraphQL endpoints cannot be disabled at the same time.
        if (!runtimeConfig.IsRestEnabled && !runtimeConfig.IsGraphQLEnabled)
        {
            HandleOrRecordException(new DataApiBuilderException(
                message: $"Both GraphQL and REST endpoints are disabled.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
        }

        string? runtimeBaseRoute = runtimeConfig.Runtime?.BaseRoute;

        // Ensure that the runtime base-route is only configured when authentication provider is StaticWebApps.
        if (runtimeBaseRoute is not null)
        {
            if (!runtimeConfig.IsStaticWebAppsIdentityProvider)
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: "Runtime base-route can only be used when the authentication provider is Static Web Apps.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }

            if (!TryValidateUriComponent(runtimeBaseRoute, out string exceptionMsgSuffix))
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: $"Runtime base-route {exceptionMsgSuffix}",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }
        }

        ValidateRestURI(runtimeConfig);
        ValidateGraphQLURI(runtimeConfig);
        // Do not check for conflicts if GraphQL or REST endpoints are disabled.
        if (!runtimeConfig.IsRestEnabled || !runtimeConfig.IsGraphQLEnabled)
        {
            return;
        }

        if (string.Equals(
            a: runtimeConfig.RestPath,
            b: runtimeConfig.GraphQLPath,
            comparisonType: StringComparison.OrdinalIgnoreCase))
        {
            HandleOrRecordException(new DataApiBuilderException(
                message: $"Conflicting GraphQL and REST path configuration.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
        }
    }

    /// <summary>
    /// Method to validate that the REST URI (REST path prefix, REST base route).
    /// Skips validation for cosmosDB since it doesn't support REST.
    /// </summary>
    /// <param name="runtimeConfig"></param>
    public void ValidateRestURI(RuntimeConfig runtimeConfig)
    {
        if (runtimeConfig.ListAllDataSources().All(x => x.DatabaseType is DatabaseType.CosmosDB_NoSQL))
        {
            // if all dbs are cosmos no rest support.
            return;
        }

        // validate the rest path.
        string restPath = runtimeConfig.RestPath;
        if (!TryValidateUriComponent(restPath, out string exceptionMsgSuffix))
        {
            HandleOrRecordException(new DataApiBuilderException(
                message: $"{ApiType.REST} path {exceptionMsgSuffix}",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
        }

    }

    /// <summary>
    /// Method to validate that the GraphQL URI (GraphQL path prefix).
    /// </summary>
    /// <param name="runtimeConfig"></param>
    public void ValidateGraphQLURI(RuntimeConfig runtimeConfig)
    {
        string graphqlPath = runtimeConfig.GraphQLPath;
        if (!TryValidateUriComponent(graphqlPath, out string exceptionMsgSuffix))
        {
            HandleOrRecordException(new DataApiBuilderException(
                message: $"{ApiType.GraphQL} path {exceptionMsgSuffix}",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
        }
    }

    /// <summary>
    /// Method to validate that the REST/GraphQL URI component is well formed and does not contain
    /// any reserved characters. In case the URI component is not well formed the exception message containing
    /// the reason for ill-formed URI component is returned. Else we return an empty string.
    /// </summary>
    /// <param name="uriComponent">path prefix/base route for rest/graphql apis</param>
    /// <returns>false when the URI component is not well formed.</returns>
    private static bool TryValidateUriComponent(string? uriComponent, out string exceptionMessageSuffix)
    {
        exceptionMessageSuffix = string.Empty;
        if (string.IsNullOrEmpty(uriComponent))
        {
            exceptionMessageSuffix = "cannot be null or empty.";
        }
        // A valid URI component should start with a forward slash '/'.
        else if (!uriComponent.StartsWith("/"))
        {
            exceptionMessageSuffix = "should start with a '/'.";
        }
        else
        {
            uriComponent = uriComponent.Substring(1);
            // URI component should not contain any reserved characters.
            if (DoesUriComponentContainReservedChars(uriComponent))
            {
                exceptionMessageSuffix = URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG;
            }
        }

        return string.IsNullOrEmpty(exceptionMessageSuffix);
    }

    /// <summary>
    /// Method to validate that the REST/GraphQL API's URI component does not contain
    /// any reserved characters.
    /// </summary>
    /// <param name="uriComponent">path prefix for rest/graphql apis</param>
    public static bool DoesUriComponentContainReservedChars(string uriComponent)
    {
        return _reservedUriCharsRgx.IsMatch(uriComponent);
    }

    private void ValidateAuthenticationOptions(RuntimeConfig runtimeConfig)
    {
        // Bypass validation of auth if there is no auth provided
        if (runtimeConfig.Runtime?.Host?.Authentication is null)
        {
            return;
        }

        bool isAudienceSet = !string.IsNullOrEmpty(runtimeConfig.Runtime.Host.Authentication.Jwt?.Audience);
        bool isIssuerSet = !string.IsNullOrEmpty(runtimeConfig.Runtime.Host.Authentication.Jwt?.Issuer);

        try
        {
            if (runtimeConfig.Runtime.Host.Authentication.IsJwtConfiguredIdentityProvider() &&
                (!isAudienceSet || !isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer must be set when using a JWT identity Provider.");
            }

            if ((!runtimeConfig.Runtime.Host.Authentication.IsJwtConfiguredIdentityProvider()) &&
                (isAudienceSet || isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer can not be set when a JWT identity provider is not configured.");
            }
        }
        catch (NotSupportedException e)
        {
            HandleOrRecordException(e);
        }
    }

    /// <summary>
    /// Validates the semantic correctness of the permissions defined for each entity within runtime configuration.
    /// </summary>
    /// <exception cref="DataApiBuilderException">Throws exception when permission validation fails.</exception>
    public void ValidatePermissionsInConfig(RuntimeConfig runtimeConfig)
    {
        foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
        {
            HashSet<EntityActionOperation> totalSupportedOperationsFromAllRoles = new();
            foreach (EntityPermission permissionSetting in entity.Permissions)
            {
                string roleName = permissionSetting.Role;
                EntityAction[] actions = permissionSetting.Actions;
                List<EntityActionOperation> operationsList = new();
                foreach (EntityAction action in actions)
                {
                    try
                    {
                        if (action is null)
                        {
                            throw GetInvalidActionException(entityName, roleName, actionName: "null");
                        }

                        // Evaluate actionOp as the current operation to be validated.
                        EntityActionOperation actionOp = action.Action;

                        // If we have reached this point, it means that we don't have any invalid
                        // data type in actions. However we need to ensure that the actionOp is valid.
                        if (!IsValidPermissionAction(actionOp, entity, entityName))
                        {
                            throw GetInvalidActionException(entityName, roleName, actionOp.ToString());
                        }

                        if (action.Fields is not null)
                        {
                            // Check if the IncludeSet/ExcludeSet contain wildcard. If they contain wildcard, we make sure that they
                            // don't contain any other field. If they do, we HandleOrRecordException(an appropriate exception.
                            if (action.Fields.Include is not null && action.Fields.Include.Contains(AuthorizationResolver.WILDCARD)
                                && action.Fields.Include.Count > 1 ||
                                action.Fields.Exclude.Contains(AuthorizationResolver.WILDCARD) && action.Fields.Exclude.Count > 1)
                            {
                                // See if included or excluded columns contain wildcard and another field.
                                // If that's the case with both of them, we specify 'included' in error.
                                string misconfiguredColumnSet = action.Fields.Exclude.Contains(AuthorizationResolver.WILDCARD)
                                    && action.Fields.Exclude.Count > 1 ? "excluded" : "included";
                                string actionName = actionOp is EntityActionOperation.All ? "*" : actionOp.ToString();

                                HandleOrRecordException(new DataApiBuilderException(
                                        message: $"No other field can be present with wildcard in the {misconfiguredColumnSet} set for:" +
                                        $" entity:{entityName}, role:{permissionSetting.Role}, action:{actionName}",
                                        statusCode: HttpStatusCode.ServiceUnavailable,
                                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                            }

                            if (action.Policy is not null && action.Policy.Database is not null)
                            {
                                // validate that all the fields mentioned in database policy are accessible to user.
                                AreFieldsAccessible(action.Policy.Database,
                                    action.Fields.Include, action.Fields.Exclude);

                                // validate that all the claimTypes in the policy are well formed.
                                ValidateClaimsInPolicy(action.Policy.Database, runtimeConfig);
                            }
                        }

                        DataSource entityDataSource = runtimeConfig.GetDataSourceFromEntityName(entityName);

                        if (entityDataSource.DatabaseType is not DatabaseType.MSSQL && !IsValidDatabasePolicyForAction(action))
                        {
                            throw new DataApiBuilderException(
                                message: $"The Create action does not support defining a database policy." +
                                $" entity:{entityName}, role:{permissionSetting.Role}, action:{action.Action}",
                                statusCode: HttpStatusCode.ServiceUnavailable,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                        }

                        operationsList.Add(actionOp);
                        totalSupportedOperationsFromAllRoles.Add(actionOp);
                    }
                    catch (Exception e)
                    {
                        HandleOrRecordException(e);
                    }
                }

                // Stored procedures only support the "execute" operation.
                if (entity.Source.Type is EntitySourceType.StoredProcedure)
                {
                    if ((operationsList.Count > 1)
                        || (operationsList.Count is 1 && !IsValidPermissionAction(operationsList[0], entity, entityName)))
                    {
                        HandleOrRecordException(new DataApiBuilderException(
                            message: $"Invalid Operations for Entity: {entityName}. " +
                                $"Stored procedures can only be configured with the 'execute' operation.",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                    }
                }
            }
        }
    }

    /// <summary>
    /// A database policy can only be defined for a PermissionOperation when
    /// the operation type is read, update, delete.
    /// A create operation (database record insert) does not support query predicates
    /// such as "WHERE name = 'xyz'"
    /// </summary>
    /// <param name="permission"></param>
    /// <returns>True/False</returns>
    public static bool IsValidDatabasePolicyForAction(EntityAction permission)
    {
        if (permission.Action is EntityActionOperation.Create)
        {
            return string.IsNullOrWhiteSpace(permission.Policy?.Database);
        }

        return true;
    }

    /// <summary>
    /// Validates the semantic correctness of an Entity's relationships in the
    /// runtime configuration against the metadata collected from the backend DB
    /// that is provided to this function via use of the MetadataProviderFactory.
    /// Validating Cases using DB metadata:
    /// 1. If the LinkingSourceFields or sourceFields and LinkingTargetFields or targetFields are not
    /// specified in the config for the given linkingObject, then the underlying database should
    /// contain a foreign key relationship between the source and target entity.
    /// 2. If linkingObject is null, and either of SourceFields or targetFields is null, then foreignKey pair
    /// between source and target entity must be defined in the DB.
    /// </summary>
    /// <exception cref="DataApiBuilderException">Throws exception whenever some validation fails.</exception>
    public void ValidateRelationships(RuntimeConfig runtimeConfig, IMetadataProviderFactory sqlMetadataProviderFactory)
    {
        // To avoid creating many lists of invalid columns we instantiate before looping through entities.
        // List.Clear() is O(1) so clearing the list, for re-use, inside of the loops is fine.
        List<string> invalidColumns = new();

        // Loop through each entity in the config and verify its relationship.
        foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
        {
            // Skipping relationship validation if entity has no relationship
            // or if graphQL is disabled.
            if (entity.Relationships is null || !entity.GraphQL.Enabled)
            {
                continue;
            }

            string databaseName = runtimeConfig.GetDataSourceNameFromEntityName(entityName);
            ISqlMetadataProvider sqlMetadataProvider = sqlMetadataProviderFactory.GetMetadataProvider(databaseName);

            foreach ((string relationshipName, EntityRelationship relationship) in entity.Relationships!)
            {
                ValidateSourceAndTargetFieldsAsBackingColumns(
                    entityName: entityName,
                    relationshipName: relationshipName,
                    relationship: relationship,
                    sqlMetadataProvider: sqlMetadataProvider,
                    invalidColumns: invalidColumns);

                // Validation to ensure DatabaseObject is correctly inferred from the entity name.
                DatabaseObject? sourceObject, targetObject;
                if (!sqlMetadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out sourceObject))
                {
                    sourceObject = null;
                    HandleOrRecordException(new DataApiBuilderException(
                        message: $"Could not infer database object for source entity: {entityName} in relationship: {relationshipName}." +
                            $" Check if the entity: {entityName} is correctly defined in the config.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                }

                if (!sqlMetadataProvider.EntityToDatabaseObject.TryGetValue(relationship.TargetEntity, out targetObject))
                {
                    targetObject = null;
                    HandleOrRecordException(new DataApiBuilderException(
                        message: $"Could not infer database object for target entity: {relationship.TargetEntity} in relationship: {relationshipName}." +
                            $" Check if the entity: {relationship.TargetEntity} is correctly defined in the config.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                }

                if (sourceObject is null || targetObject is null)
                {
                    continue;
                }

                DatabaseTable sourceDatabaseObject = (DatabaseTable)sourceObject;
                DatabaseTable targetDatabaseObject = (DatabaseTable)targetObject;
                if (relationship.LinkingObject is not null)
                {
                    (string linkingTableSchema, string linkingTableName) = sqlMetadataProvider.ParseSchemaAndDbTableName(relationship.LinkingObject)!;
                    DatabaseTable linkingDatabaseObject = new(linkingTableSchema, linkingTableName);

                    if (relationship.LinkingSourceFields is null || relationship.SourceFields is null)
                    {
                        if (!sqlMetadataProvider.VerifyForeignKeyExistsInDB(linkingDatabaseObject, sourceDatabaseObject))
                        {
                            HandleOrRecordException(new DataApiBuilderException(
                            message: $"Could not find relationship between Linking Object: {relationship.LinkingObject}" +
                                $" and entity: {entityName}.",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                        }
                    }

                    if (relationship.LinkingTargetFields is null || relationship.TargetFields is null)
                    {
                        if (!sqlMetadataProvider.VerifyForeignKeyExistsInDB(linkingDatabaseObject, targetDatabaseObject))
                        {
                            HandleOrRecordException(new DataApiBuilderException(
                            message: $"Could not find relationship between Linking Object: {relationship.LinkingObject}" +
                                $" and entity: {relationship.TargetEntity}.",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                        }
                    }

                    if (!_runtimeConfigProvider.IsLateConfigured)
                    {
                        string sourceDBOName = sqlMetadataProvider.EntityToDatabaseObject[entityName].FullName;
                        string targetDBOName = sqlMetadataProvider.EntityToDatabaseObject[relationship.TargetEntity].FullName;
                        string cardinality = relationship.Cardinality.ToString().ToLower();
                        RelationShipPair linkedSourceRelationshipPair = new(linkingDatabaseObject, sourceDatabaseObject);
                        RelationShipPair linkedTargetRelationshipPair = new(linkingDatabaseObject, targetDatabaseObject);
                        ForeignKeyDefinition? fKDef;
                        string referencedSourceColumns = relationship.SourceFields is not null ? string.Join(",", relationship.SourceFields) :
                            sqlMetadataProvider.PairToFkDefinition!.TryGetValue(linkedSourceRelationshipPair, out fKDef) ?
                            string.Join(",", fKDef.ReferencedColumns) : string.Empty;
                        string referencingSourceColumns = relationship.LinkingSourceFields is not null ? string.Join(",", relationship.LinkingSourceFields) :
                            sqlMetadataProvider.PairToFkDefinition!.TryGetValue(linkedSourceRelationshipPair, out fKDef) ?
                            string.Join(",", fKDef.ReferencingColumns) : string.Empty;
                        string referencedTargetColumns = relationship.TargetFields is not null ? string.Join(",", relationship.TargetFields) :
                            sqlMetadataProvider.PairToFkDefinition!.TryGetValue(linkedTargetRelationshipPair, out fKDef) ?
                            string.Join(",", fKDef.ReferencedColumns) : string.Empty;
                        string referencingTargetColumns = relationship.LinkingTargetFields is not null ? string.Join(",", relationship.LinkingTargetFields) :
                            sqlMetadataProvider.PairToFkDefinition!.TryGetValue(linkedTargetRelationshipPair, out fKDef) ?
                            string.Join(",", fKDef.ReferencingColumns) : string.Empty;

                        _logger.LogDebug(
                            message: "{entityName}: {sourceDBOName}({referencedSourceColumns}) is related to {cardinality} " +
                            "{relationship.TargetEntity}: {targetDBOName}({referencedTargetColumns}) by " +
                            "{relationship.LinkingObject}(linking.source.fields: {referencingSourceColumns}), (linking.target.fields: {referencingTargetColumns})",
                            entityName,
                            sourceDBOName,
                            referencedSourceColumns,
                            cardinality,
                            relationship.TargetEntity,
                            targetDBOName,
                            referencedTargetColumns,
                            relationship.LinkingObject,
                            referencingSourceColumns,
                            referencingTargetColumns);
                    }
                }

                if (relationship.LinkingObject is null
                    && (relationship.SourceFields is null || relationship.TargetFields is null))
                {
                    if (!sqlMetadataProvider.VerifyForeignKeyExistsInDB(sourceDatabaseObject, targetDatabaseObject))
                    {
                        HandleOrRecordException(new DataApiBuilderException(
                            message: $"Could not find relationship between entities: {entityName} and {relationship.TargetEntity}.",
                            statusCode: HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
                    }
                }

                if (relationship.LinkingObject is null && !_runtimeConfigProvider.IsLateConfigured)
                {
                    RelationShipPair sourceTargetRelationshipPair = new(sourceDatabaseObject, targetDatabaseObject);
                    RelationShipPair targetSourceRelationshipPair = new(targetDatabaseObject, sourceDatabaseObject);
                    string sourceDBOName = sqlMetadataProvider.EntityToDatabaseObject[entityName].FullName;
                    string targetDBOName = sqlMetadataProvider.EntityToDatabaseObject[relationship.TargetEntity].FullName;
                    string cardinality = relationship.Cardinality.ToString().ToLower();
                    ForeignKeyDefinition? fKDef;
                    string sourceColumns = relationship.SourceFields is not null ? string.Join(",", relationship.SourceFields) :
                        sqlMetadataProvider.PairToFkDefinition!.TryGetValue(sourceTargetRelationshipPair, out fKDef) ?
                        string.Join(",", fKDef.ReferencingColumns) :
                        sqlMetadataProvider.PairToFkDefinition!.TryGetValue(targetSourceRelationshipPair, out fKDef) ?
                        string.Join(",", fKDef.ReferencedColumns) : string.Empty;
                    string targetColumns = relationship.TargetFields is not null ? string.Join(",", relationship.TargetFields) :
                        sqlMetadataProvider.PairToFkDefinition!.TryGetValue(sourceTargetRelationshipPair, out fKDef) ?
                        string.Join(",", fKDef.ReferencedColumns) :
                        sqlMetadataProvider.PairToFkDefinition!.TryGetValue(targetSourceRelationshipPair, out fKDef) ?
                        string.Join(",", fKDef.ReferencingColumns) : string.Empty;

                    _logger.LogDebug(
                        message: "{entityName}: {sourceDBOName}({sourceColumns}) is related to {cardinality} {relationshipTargetEntity}: {targetDBOName}({targetColumns}).",
                        entityName,
                        sourceDBOName,
                        sourceColumns,
                        cardinality,
                        relationship.TargetEntity,
                        targetDBOName,
                        targetColumns
                        );
                }
            }
        }
    }

    /// <summary>
    /// Handles the validation of source and target fields as valid backing columns in the DB.
    /// </summary>
    /// <param name="relationshipName">Name of the relationship.</param>
    /// <param name="entityName">The name of the entity that we check for backing columns.</param>
    /// <param name="relationship">The relationship that holds the fields to validate.</param>
    /// <param name="sqlMetadataProvider">The sqlMetadataProvider used to lookup if the backing fields are valid columns in DB.</param>
    /// <param name="invalidColumns">List in which invalid fields are aggregated, this list may be modified by this function.</param>
    private void ValidateSourceAndTargetFieldsAsBackingColumns(
        string entityName,
        string relationshipName,
        EntityRelationship relationship,
        ISqlMetadataProvider sqlMetadataProvider,
        List<string> invalidColumns)
    {
        // In all kinds of relationships, if sourceFields are included they must be valid columns in the backend.
        if (relationship.SourceFields is not null)
        {
            GetFieldsNotBackedByColumnsInDB(
                invalidColumns: invalidColumns,
                fields: relationship.SourceFields,
                entityName: entityName,
                sqlMetadataProvider: sqlMetadataProvider);

            if (invalidColumns.Count > 0)
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: $"Entity: {entityName} has a relationship: {relationshipName} with source fields: {string.Join(",", invalidColumns)} that " +
                        $"do not exist as columns in entity: {entityName}.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }
        }

        // In all kinds of relationships, if targetFields are included they must be valid columns in the backend.
        if (relationship.TargetFields is not null)
        {
            GetFieldsNotBackedByColumnsInDB(
                invalidColumns: invalidColumns,
                fields: relationship.TargetFields,
                entityName: relationship.TargetEntity,
                sqlMetadataProvider: sqlMetadataProvider);

            if (invalidColumns.Count > 0)
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: $"Entity: {entityName} has a relationship: {relationshipName} with target fields: {string.Join(",", invalidColumns)} that " +
                        $"do not exist as columns in entity: {relationship.TargetEntity}.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }
        }
    }

    /// <summary>
    /// This helper function checks if the elements of fields exist as valid backing columns
    /// in the DB associated with the provided entity name. Those fields that do not exist
    /// as valid backing columns are added to the provided list of string invalidColumns. This
    /// works because C# is pass by reference for referenced class types.
    /// </summary>
    /// <param name="invalidColumns">List in which to aggregate the invalid fields.</param>
    /// <param name="fields">List of the backing fields to check for existence in backing DB.</param>
    /// <param name="entityName">The name of the entity that we check for backing columns.</param>
    /// <param name="sqlMetadataProvider">The sqlMetadataProvider used to lookup if the backing fields are valid columns in DB.</param>
    private static void GetFieldsNotBackedByColumnsInDB(
        List<string> invalidColumns,
        string[] fields,
        string entityName,
        ISqlMetadataProvider sqlMetadataProvider)
    {
        invalidColumns.Clear();
        foreach (string field in fields)
        {
            // We call this function because the keyset are the backing columns
            // which is what want to validate.
            if (!sqlMetadataProvider.TryGetExposedColumnName(entityName, field, out _))
            {
                invalidColumns.Add(field);
            }
        }
    }

    /// <summary>
    /// Helper function for validating the fields and associated linking fields in a many-many relationship.
    /// Validates that if one exists, the other exists as well, and that if both exist that their sizes are
    /// the same.
    /// </summary>
    /// <param name="fields">Either source of target fields.</param>
    /// <param name="linkingFields">Associated linking fields.</param>
    /// <param name="fieldType">The type of fields, either source or target.</param>
    /// <param name="entityName">Name of the entity that holds the relationship.</param>
    /// <param name="relationshipName">Name of the relationship.</param>
    private void ValidateFieldsAndAssociatedLinkingFields(string[]? fields, string[]? linkingFields, string fieldType, string entityName, string relationshipName)
    {
        // Validation to ensure that fields and linkingFields are either both null or both non null.
        if (fields is null ^ linkingFields is null)
        {
            HandleOrRecordException(new DataApiBuilderException(
                message: $"Entity: {entityName} has a many-many relationship: {relationshipName}, which has {fieldType} and associated linking fields " +
                    $"where one is null and the other is not.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
        }

        // When both fields and linkingTarget fields exist for a many-many relationship their size must match.
        if (fields is not null && linkingFields is not null && fields.Length != linkingFields.Length)
        {
            HandleOrRecordException(new DataApiBuilderException(
                message: $"Entity: {entityName} has a many-many relationship: {relationshipName} with {fields.Length} " +
                    $"{fieldType} fields defined, but {linkingFields.Length} linking {fieldType} fields defined.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
        }
    }

    /// <summary>
    /// Method to do different validations on claims in the policy.
    /// </summary>
    /// <param name="policy">The policy to be validated and processed.</param>
    /// <returns>Processed policy</returns>
    /// <exception cref="DataApiBuilderException">Throws exception when one or the other validations fail.</exception>
    private void ValidateClaimsInPolicy(string policy, RuntimeConfig runtimeConfig)
    {
        // Find all the claimTypes from the policy
        MatchCollection claimTypes = GetClaimTypesInPolicy(policy);
        bool isStaticWebAppsAuthConfigured = runtimeConfig.IsStaticWebAppsIdentityProvider;

        foreach (Match claimType in claimTypes)
        {
            try
            {
                // Remove the prefix @claims. from the claimType
                string typeOfClaim = claimType.Value.Substring(AuthorizationResolver.CLAIM_PREFIX.Length);

                if (string.IsNullOrWhiteSpace(typeOfClaim))
                {
                    // Empty claimType is not allowed
                    throw new DataApiBuilderException(
                        message: $"ClaimType cannot be empty.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError
                        );
                }

                if (_invalidClaimCharsRgx.IsMatch(typeOfClaim))
                {
                    // Not a valid claimType containing allowed characters
                    throw new DataApiBuilderException(
                        message: $"Invalid format for claim type {typeOfClaim} supplied in policy.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError
                        );
                }

                if (isStaticWebAppsAuthConfigured &&
                    !(typeOfClaim.Equals(StaticWebAppsAuthentication.USER_ID_CLAIM) ||
                    typeOfClaim.Equals(StaticWebAppsAuthentication.USER_DETAILS_CLAIM)))
                {
                    // Not a valid claimType containing allowed characters
                    throw new DataApiBuilderException(
                        message: INVALID_CLAIMS_IN_POLICY_ERR_MSG,
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError
                        );
                }
            }
            catch (Exception e)
            {
                HandleOrRecordException(e);
            }
        } // MatchType claimType
    }

    /// <summary>
    /// Helper method which takes in the policy string and checks whether all fields referenced
    /// within the policy are accessible to the user.
    /// </summary>
    /// <param name="policy">Database policy</param>
    /// <param name="include">Array of fields which are accessible to the user.</param>
    /// <param name="exclude">Array of fields which are not accessible to the user.</param>
    private void AreFieldsAccessible(string policy, HashSet<string>? includedFields, HashSet<string> excludedFields)
    {
        // Pattern of field references in the policy
        string fieldCharsRgx = @"@item\.[a-zA-Z0-9_]*";
        MatchCollection fieldNameMatches = Regex.Matches(policy, fieldCharsRgx);

        foreach (Match fieldNameMatch in fieldNameMatches)
        {
            if (!IsFieldAccessible(fieldNameMatch, includedFields, excludedFields))
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: $"Not all the columns required by policy are accessible.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError)
                );
            }
        }
    }

    /// <summary>
    /// Helper method which takes in field prefixed with @item. directive and check if its
    /// accessible based on include/exclude fields.
    /// </summary>
    /// <param name="columnNameMatch"></param>
    /// <param name="included">Set of fields which are accessible to the user.</param>
    /// <param name="excluded">Set of fields which are not accessible to the user.</param>
    /// <returns>Boolean value indicating whether the field is accessible or not.</returns>
    /// <exception cref="DataApiBuilderException">Throws exception if the field is not accessible.</exception>
    private static bool IsFieldAccessible(Match columnNameMatch, HashSet<string>? includedFields, HashSet<string> excludedFields)
    {
        string columnName = columnNameMatch.Value.Substring(AuthorizationResolver.FIELD_PREFIX.Length);
        if (excludedFields.Contains(columnName!) || excludedFields.Contains(AuthorizationResolver.WILDCARD) ||
            (includedFields is not null && !includedFields.Contains(AuthorizationResolver.WILDCARD) && !includedFields.Contains(columnName)))
        {
            // If column is present in excluded OR excluded='*'
            // If column is absent from included and included!=*
            // In this case, the column is not accessible to the user
            return false;
        }

        return true;
    }

    /// <summary>
    /// Helper method to get all the claimTypes from the policy.
    /// </summary>
    /// <param name="policy">Policy from which the claimTypes are to be looked</param>
    /// <returns>Collection of all matches i.e. claimTypes.</returns>
    private static MatchCollection GetClaimTypesInPolicy(string policy)
    {
        return Regex.Matches(policy, _claimChars);
    }

    /// <summary>
    /// Helper function to create a DataApiBuilderException object.
    /// Called when the actionName is not valid.
    /// </summary>
    /// <param name="entityName">Name of entity for which invalid action is supplied.</param>
    /// <param name="roleName">Name of role for which invalid action is supplied.</param>
    /// <param name="actionName">Name of invalid action.</param>
    /// <returns></returns>
    private static DataApiBuilderException GetInvalidActionException(string entityName, string roleName, string actionName)
    {
        return new DataApiBuilderException(
            message: $"action:{actionName} specified for entity:{entityName}, role:{roleName} is not valid.",
            statusCode: HttpStatusCode.ServiceUnavailable,
            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
    }

    private void HandleOrRecordException(Exception ex)
    {
        if (_isValidateOnly)
        {
            ConfigValidationExceptions.Add(ex);
        }
        else
        {
            throw ex;
        }
    }

    /// <summary>
    /// Returns whether the action is a valid
    /// Valid non stored procedure actions:
    /// - Create, Read, Update, Delete (CRUD)
    /// - All (*)
    /// Valid stored procedure  actions:
    /// - Execute
    /// </summary>
    /// <param name="action">Compared against valid actions to determine validity.</param>
    /// <param name="entity">Used to identify entity's representative object type.</param>
    /// <param name="entityName">Used to supplement error messages.</param>
    /// <returns>Boolean value indicating whether the action is valid or not.</returns>
    private bool IsValidPermissionAction(EntityActionOperation action, Entity entity, string entityName)
    {
        if (entity.Source.Type is EntitySourceType.StoredProcedure)
        {
            if (action is not EntityActionOperation.All && !EntityAction.ValidStoredProcedurePermissionOperations.Contains(action))
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: $"Invalid operation for Entity: {entityName}. Stored procedures can only be configured with the 'execute' operation.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }

            return true;
        }
        else
        {
            if (action is EntityActionOperation.Execute)
            {
                HandleOrRecordException(new DataApiBuilderException(
                    message: $"Invalid operation for Entity: {entityName}. The 'execute' operation can only be configured for entities backed by stored procedures.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError));
            }

            return action is EntityActionOperation.All || EntityAction.ValidPermissionOperations.Contains(action);
        }
    }
}
