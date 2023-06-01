// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.AuthenticationHelpers;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using PermissionOperation = Azure.DataApiBuilder.Config.PermissionOperation;

namespace Azure.DataApiBuilder.Service.Configurations
{
    /// <summary>
    /// This class encapsulates methods to validate the runtime config file.
    /// </summary>
    public class RuntimeConfigValidator : IConfigValidator
    {
        private readonly IFileSystem _fileSystem;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly ILogger<RuntimeConfigValidator> _logger;

        // Only characters from a-z,A-Z,0-9,.,_ are allowed to be present within the claimType.
        private static readonly string _invalidClaimChars = @"[^a-zA-Z0-9_\.]+";

        // Regex to check occurrence of any character not among [a-z,A-Z,0-9,.,_] in the claimType.
        // The claimType is invalid if there is a match found.
        private static readonly Regex _invalidClaimCharsRgx = new(_invalidClaimChars, RegexOptions.Compiled);

        // "Reserved characters as defined in RFC3986 are not allowed to be present in the
        // REST/GraphQL custom path because they are not acceptable to be present in URIs.
        // " Refer here: https://www.rfc-editor.org/rfc/rfc3986#page-12.
        private static readonly string _invalidPathChars = @"[\.:\?#/\[\]@!$&'()\*\+,;=]+";

        //  Regex to validate rest/graphql custom path prefix.
        public static readonly Regex _invalidApiPathCharsRgx = new(_invalidPathChars, RegexOptions.Compiled);

        // Regex used to extract all claimTypes in policy. It finds all the substrings which are
        // of the form @claims.*** delimited by space character,end of the line or end of the string.
        private static readonly string _claimChars = @"@claims\.[^\s\)]*";

        // actionKey is the key used in json runtime config to
        // specify the action name.
        private static readonly string _actionKey = "action";

        // Error messages.
        public const string INVALID_CLAIMS_IN_POLICY_ERR_MSG = "One or more claim types supplied in the database policy are not supported.";
        public const string INVALID_REST_PATH_WITH_RESERVED_CHAR_ERR_MSG = "REST path contains one or more reserved characters.";
        public const string INVALID_GRAPHQL_PATH_WITH_RESERVED_CHAR_ERR_MSG = "GraphQL path contains one or more reserved characters.";

        public RuntimeConfigValidator(
            RuntimeConfigProvider runtimeConfigProvider,
            IFileSystem fileSystem,
            ILogger<RuntimeConfigValidator> logger)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
            _fileSystem = fileSystem;
            _logger = logger;
        }

        /// <summary>
        /// The driver for validation of the runtime configuration file.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public void ValidateConfig()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetRuntimeConfiguration();

            ValidateDataSourceInConfig(
                runtimeConfig,
                _fileSystem,
                _logger);

            ValidateAuthenticationConfig();
            ValidateGlobalEndpointRouteConfig(runtimeConfig);

            // Running these graphQL validations only in development mode to ensure
            // fast startup of engine in production mode.
            if (runtimeConfig.GraphQLGlobalSettings.Enabled
                 && runtimeConfig.HostGlobalSettings.Mode is HostModeType.Development)
            {
                ValidateEntityNamesInConfig(runtimeConfig.Entities);
                ValidateEntitiesDoNotGenerateDuplicateQueriesOrMutation(runtimeConfig.Entities);
            }
        }

        /// <summary>
        /// Throws exception if Invalid connection-string or database type
        /// is present in the config
        /// </summary>
        public static void ValidateDataSourceInConfig(
            RuntimeConfig runtimeConfig,
            IFileSystem fileSystem,
            ILogger logger)
        {
            // Connection string can't be null or empty
            if (string.IsNullOrWhiteSpace(runtimeConfig.ConnectionString))
            {
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            ValidateDatabaseType(runtimeConfig, fileSystem, logger);
        }

        /// <summary>
        /// Throws exception if database type is incorrectly configured
        /// in the config. 
        /// </summary>
        public static void ValidateDatabaseType(
            RuntimeConfig runtimeConfig,
            IFileSystem fileSystem,
            ILogger logger)
        {
            // Database Type cannot be null or empty
            if (string.IsNullOrWhiteSpace(runtimeConfig.DatabaseType.ToString()))
            {
                const string databaseTypeNotSpecified =
                    "The database-type should be provided with the runtime config.";
                logger.LogCritical(databaseTypeNotSpecified);
                throw new NotSupportedException(databaseTypeNotSpecified);
            }

            // Schema file should be present in the directory if not specified in the config
            // when using cosmosdb_nosql database.
            if (runtimeConfig.DatabaseType is DatabaseType.cosmosdb_nosql)
            {
                CosmosDbNoSqlOptions cosmosDbNoSql = runtimeConfig.DataSource.CosmosDbNoSql!;
                if (cosmosDbNoSql is null)
                {
                    throw new NotSupportedException("CosmosDB_NoSql is specified but no CosmosDB_NoSql configuration information has been provided.");
                }

                if (string.IsNullOrEmpty(cosmosDbNoSql.GraphQLSchema))
                {
                    if (string.IsNullOrEmpty(cosmosDbNoSql.GraphQLSchemaPath))
                    {
                        throw new NotSupportedException("No GraphQL schema file has been provided for CosmosDB_NoSql. Ensure you provide a GraphQL schema containing the GraphQL object types to expose.");
                    }

                    if (!fileSystem.File.Exists(cosmosDbNoSql.GraphQLSchemaPath))
                    {
                        throw new FileNotFoundException($"The GraphQL schema file at '{cosmosDbNoSql.GraphQLSchemaPath}' could not be found. Ensure that it is a path relative to the runtime.");
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
        /// </summary>
        /// <param name="entityCollection">Entity definitions</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public static void ValidateEntitiesDoNotGenerateDuplicateQueriesOrMutation(IDictionary<string, Entity> entityCollection)
        {
            HashSet<string> graphQLOperationNames = new();

            foreach ((string entityName, Entity entity) in entityCollection)
            {
                entity.TryPopulateSourceFields();
                if (entity.GraphQL is null
                    || (entity.GraphQL is bool graphQLEnabled && !graphQLEnabled))
                {
                    continue;
                }

                bool containsDuplicateOperationNames = false;
                if (entity.ObjectType is SourceType.StoredProcedure)
                {
                    // For Stored Procedures a single query/mutation is generated.
                    string storedProcedureQueryName = GenerateStoredProcedureGraphQLFieldName(entityName, entity);

                    if (!graphQLOperationNames.Add(storedProcedureQueryName))
                    {
                        containsDuplicateOperationNames = true;
                    }
                }
                else
                {
                    // For entities (table/view) that have graphQL exposed, two queries and three mutations would be generated.
                    // Primary Key Query: For fetching an item using its primary key.
                    // List Query: To fetch a paginated list of items.
                    // Query names for both these queries are determined.
                    string pkQueryName = GenerateByPKQueryName(entityName, entity);
                    string listQueryName = GenerateListQueryName(entityName, entity);

                    // Mutations names for the exposed entities are determined.
                    string createMutationName = $"create{GetDefinedSingularName(entityName, entity)}";
                    string updateMutationName = $"update{GetDefinedSingularName(entityName, entity)}";
                    string deleteMutationName = $"delete{GetDefinedSingularName(entityName, entity)}";

                    if (!graphQLOperationNames.Add(pkQueryName)
                        || !graphQLOperationNames.Add(listQueryName)
                        || !graphQLOperationNames.Add(createMutationName)
                        || !graphQLOperationNames.Add(updateMutationName)
                        || !graphQLOperationNames.Add(deleteMutationName))
                    {
                        containsDuplicateOperationNames = true;
                    }
                }

                if (containsDuplicateOperationNames)
                {
                    throw new DataApiBuilderException(
                        message: $"Entity {entityName} generates queries/mutation that already exist",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                }
            }
        }

        /// <summary>
        /// Check whether the entity name defined in runtime config only contains
        /// characters allowed for GraphQL names.
        /// Does not perform validation for entities which do not
        /// have GraphQL configuration: when entity.GraphQL == false or null.
        /// </summary>
        /// <seealso cref="https://spec.graphql.org/October2021/#Name"/>
        /// <param name="runtimeConfig"></param>
        public static void ValidateEntityNamesInConfig(Dictionary<string, Entity> entityCollection)
        {
            foreach (string entityName in entityCollection.Keys)
            {
                Entity entity = entityCollection[entityName];

                if (entity.GraphQL is null)
                {
                    continue;
                }
                else if (entity.GraphQL is bool graphQLEnabled)
                {
                    if (!graphQLEnabled)
                    {
                        continue;
                    }

                    ValidateNameRequirements(entityName);
                }
                else if (entity.GraphQL is GraphQLEntitySettings graphQLSettings)
                {
                    ValidateGraphQLEntitySettings(graphQLSettings.Type);
                }
                else if (entity.GraphQL is GraphQLStoredProcedureEntityVerboseSettings graphQLVerboseSettings)
                {
                    ValidateGraphQLEntitySettings(graphQLVerboseSettings.Type);
                }
            }
        }

        /// <summary>
        /// Validates a GraphQL entity's Type configuration, which involves checking
        /// whether the string value, if present, is a valid GraphQL name
        /// whether the SingularPlural value, if present, are valid GraphQL names.
        /// </summary>
        /// <param name="graphQLEntitySettingsType">object which is a string or a SingularPlural type.</param>
        private static void ValidateGraphQLEntitySettings(object? graphQLEntitySettingsType)
        {
            if (graphQLEntitySettingsType is string graphQLName)
            {
                ValidateNameRequirements(graphQLName);
            }
            else if (graphQLEntitySettingsType is SingularPlural singularPluralSettings)
            {
                ValidateNameRequirements(singularPluralSettings.Singular);

                if (singularPluralSettings.Plural is not null)
                {
                    ValidateNameRequirements(singularPluralSettings.Plural);
                }
            }
        }

        private static void ValidateNameRequirements(string entityName)
        {
            if (GraphQLNaming.ViolatesNamePrefixRequirements(entityName) ||
                GraphQLNaming.ViolatesNameRequirements(entityName))
            {
                throw new DataApiBuilderException(
                    message: $"Entity {entityName} contains characters disallowed by GraphQL.",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
            }
        }

        /// <summary>
        /// Ensure the global REST and GraphQL endpoints do not conflict if both
        /// are enabled.
        /// </summary>
        /// <param name="runtimeConfig"></param>
        public static void ValidateGlobalEndpointRouteConfig(RuntimeConfig runtimeConfig)
        {
            // Both REST and GraphQL endpoints cannot be disabled at the same time.
            if (!runtimeConfig.RestGlobalSettings.Enabled && !runtimeConfig.GraphQLGlobalSettings.Enabled)
            {
                throw new DataApiBuilderException(
                    message: $"Both GraphQL and REST endpoints are disabled.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
            }

            ValidateRestPathForRelationalDbs(runtimeConfig);
            ValidateGraphQLPath(runtimeConfig);
            // Do not check for conflicts if GraphQL or REST endpoints are disabled.
            if (!runtimeConfig.GraphQLGlobalSettings.Enabled || !runtimeConfig.RestGlobalSettings.Enabled)
            {
                return;
            }

            if (string.Equals(
                a: runtimeConfig.GraphQLGlobalSettings.Path,
                b: runtimeConfig.RestGlobalSettings.Path,
                comparisonType: StringComparison.OrdinalIgnoreCase))
            {
                throw new DataApiBuilderException(
                    message: $"Conflicting GraphQL and REST path configuration.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
            }
        }

        /// <summary>
        /// Method to validate that the rest path prefix.
        /// Skips validation for cosmosDB since it doesn't support REST.
        /// </summary>
        /// <param name="runtimeConfig"></param>
        public static void ValidateRestPathForRelationalDbs(RuntimeConfig runtimeConfig)
        {
            // cosmosdb_nosql does not support rest. No need to do any validations.
            if (runtimeConfig.DatabaseType is DatabaseType.cosmosdb_nosql)
            {
                return;
            }

            string restPath = runtimeConfig.RestGlobalSettings.Path;

            ValidateApiPath(restPath, ApiType.REST);
        }

        /// <summary>
        /// Method to validate that the GraphQL path prefix.
        /// </summary>
        /// <param name="runtimeConfig"></param>
        public static void ValidateGraphQLPath(RuntimeConfig runtimeConfig)
        {
            string graphqlPath = runtimeConfig.GraphQLGlobalSettings.Path;

            ValidateApiPath(graphqlPath, ApiType.GraphQL);
        }

        /// <summary>
        /// Method to validate that the REST/GraphQL path prefix is well formed and does not contain
        /// any forbidden characters.
        /// </summary>
        /// <param name="apiPath">path prefix for rest/graphql apis</param>
        /// <param name="apiType">Either REST or GraphQL</param>
        /// <exception cref="DataApiBuilderException"></exception>
        private static void ValidateApiPath(string apiPath, ApiType apiType)
        {
            if (string.IsNullOrEmpty(apiPath))
            {
                throw new DataApiBuilderException(
                    message: $"{apiType} path prefix cannot be null or empty.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
            }

            // A valid api path prefix should start with a forward slash '/'.
            if (!apiPath.StartsWith("/"))
            {
                throw new DataApiBuilderException(
                    message: $"{apiType} path should start with a '/'.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
            }

            apiPath = apiPath.Substring(1);

            // API path prefix should not contain any reserved characters.
            DoApiPathInvalidCharCheck(apiPath, apiType);
        }

        /// <summary>
        /// Method to validate that the REST/GraphQL path prefix does not contain
        /// any forbidden characters.
        /// </summary>
        /// <param name="apiPath">path prefix for rest/graphql apis</param>
        /// <param name="apiType">Either REST or GraphQL</param>
        /// <exception cref="DataApiBuilderException"></exception>
        public static void DoApiPathInvalidCharCheck(string apiPath, ApiType apiType)
        {
            if (_invalidApiPathCharsRgx.IsMatch(apiPath))
            {
                string errorMessage = INVALID_GRAPHQL_PATH_WITH_RESERVED_CHAR_ERR_MSG;
                if (apiType is ApiType.REST)
                {
                    errorMessage = INVALID_REST_PATH_WITH_RESERVED_CHAR_ERR_MSG;
                }

                throw new DataApiBuilderException(
                    message: errorMessage,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
            }
        }

        private void ValidateAuthenticationConfig()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetRuntimeConfiguration();

            bool isAudienceSet =
                runtimeConfig.AuthNConfig is not null &&
                runtimeConfig.AuthNConfig.Jwt is not null &&
                !string.IsNullOrEmpty(runtimeConfig.AuthNConfig.Jwt.Audience);
            bool isIssuerSet =
                runtimeConfig.AuthNConfig is not null &&
                runtimeConfig.AuthNConfig.Jwt is not null &&
                !string.IsNullOrEmpty(runtimeConfig.AuthNConfig.Jwt.Issuer);
            if ((runtimeConfig.IsJwtConfiguredIdentityProvider()) &&
                (!isAudienceSet || !isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer must be set when using a JWT identity Provider.");
            }

            if ((!runtimeConfig.IsJwtConfiguredIdentityProvider()) &&
                (isAudienceSet || isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer can not be set when a JWT identity provider is not configured.");
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
                entity.TryPopulateSourceFields();
                HashSet<Config.Operation> totalSupportedOperationsFromAllRoles = new();
                foreach (PermissionSetting permissionSetting in entity.Permissions)
                {
                    string roleName = permissionSetting.Role;
                    object[] actions = permissionSetting.Operations;
                    List<Config.Operation> operationsList = new();
                    foreach (object action in actions)
                    {
                        if (action is null)
                        {
                            throw GetInvalidActionException(entityName, roleName, actionName: "null");
                        }

                        // Evaluate actionOp as the current operation to be validated.
                        Config.Operation actionOp;
                        JsonElement actionJsonElement = JsonSerializer.SerializeToElement(action);
                        if (actionJsonElement!.ValueKind is JsonValueKind.String)
                        {
                            string actionName = action.ToString()!;
                            if (AuthorizationResolver.WILDCARD.Equals(actionName))
                            {
                                actionOp = Config.Operation.All;
                            }
                            else if (!Enum.TryParse<Config.Operation>(actionName, ignoreCase: true, out actionOp) ||
                                !IsValidPermissionAction(actionOp, entity, entityName))
                            {
                                throw GetInvalidActionException(entityName, roleName, actionName);
                            }
                        }
                        else
                        {
                            PermissionOperation configOperation;
                            try
                            {
                                configOperation = JsonSerializer.Deserialize<PermissionOperation>(action.ToString()!)!;
                            }
                            catch (Exception e)
                            {
                                throw new DataApiBuilderException(
                                    message: $"One of the action specified for entity:{entityName} is not well formed.",
                                    statusCode: HttpStatusCode.ServiceUnavailable,
                                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError,
                                    innerException: e);
                            }

                            actionOp = configOperation.Name;
                            // If we have reached this point, it means that we don't have any invalid
                            // data type in actions. However we need to ensure that the actionOp is valid.
                            if (!IsValidPermissionAction(actionOp, entity, entityName))
                            {
                                bool isActionPresent = ((JsonElement)action).TryGetProperty(_actionKey,
                                    out JsonElement actionElement);
                                if (!isActionPresent)
                                {
                                    throw new DataApiBuilderException(
                                        message: $"action cannot be omitted for entity: {entityName}, role:{roleName}",
                                        statusCode: HttpStatusCode.ServiceUnavailable,
                                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                                }

                                throw GetInvalidActionException(entityName, roleName, actionElement.ToString());
                            }

                            if (configOperation.Fields is not null)
                            {
                                // Check if the IncludeSet/ExcludeSet contain wildcard. If they contain wildcard, we make sure that they
                                // don't contain any other field. If they do, we throw an appropriate exception.
                                if (configOperation.Fields.Include is not null && configOperation.Fields.Include.Contains(AuthorizationResolver.WILDCARD)
                                    && configOperation.Fields.Include.Count > 1 ||
                                    configOperation.Fields.Exclude.Contains(AuthorizationResolver.WILDCARD) && configOperation.Fields.Exclude.Count > 1)
                                {
                                    // See if included or excluded columns contain wildcard and another field.
                                    // If thats the case with both of them, we specify 'included' in error.
                                    string misconfiguredColumnSet = configOperation.Fields.Exclude.Contains(AuthorizationResolver.WILDCARD)
                                        && configOperation.Fields.Exclude.Count > 1 ? "excluded" : "included";
                                    string actionName = actionOp is Config.Operation.All ? "*" : actionOp.ToString();

                                    throw new DataApiBuilderException(
                                            message: $"No other field can be present with wildcard in the {misconfiguredColumnSet} set for:" +
                                            $" entity:{entityName}, role:{permissionSetting.Role}, action:{actionName}",
                                            statusCode: HttpStatusCode.ServiceUnavailable,
                                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                                }

                                if (configOperation.Policy is not null && configOperation.Policy.Database is not null)
                                {
                                    // validate that all the fields mentioned in database policy are accessible to user.
                                    AreFieldsAccessible(configOperation.Policy.Database,
                                        configOperation.Fields.Include, configOperation.Fields.Exclude);

                                    // validate that all the claimTypes in the policy are well formed.
                                    ValidateClaimsInPolicy(configOperation.Policy.Database);
                                }
                            }

                            if (runtimeConfig.DatabaseType is not DatabaseType.mssql && !IsValidDatabasePolicyForAction(configOperation))
                            {
                                throw new DataApiBuilderException(
                                    message: $"The Create action does not support defining a database policy." +
                                    $" entity:{entityName}, role:{permissionSetting.Role}, action:{configOperation.Name}",
                                    statusCode: HttpStatusCode.ServiceUnavailable,
                                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                            }
                        }

                        operationsList.Add(actionOp);
                        totalSupportedOperationsFromAllRoles.Add(actionOp);
                    }

                    // Stored procedures only support the "execute" operation.
                    if (entity.ObjectType is SourceType.StoredProcedure)
                    {
                        if ((operationsList.Count > 1)
                            || (operationsList.Count is 1 && !IsValidPermissionAction(operationsList[0], entity, entityName)))
                        {
                            throw new DataApiBuilderException(
                                message: $"Invalid Operations for Entity: {entityName}. " +
                                    $"Stored procedures can only be configured with the 'execute' operation.",
                                statusCode: HttpStatusCode.ServiceUnavailable,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
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
        public bool IsValidDatabasePolicyForAction(PermissionOperation permission)
        {
            return !(permission.Policy?.Database != null && permission.Name == Config.Operation.Create);
        }

        /// <summary>
        /// Validates the semantic correctness of an Entity's relationship metadata
        /// in the runtime configuration.
        /// Validating Cases:
        /// 1. Entities not defined in the config cannot be used in a relationship.
        /// 2. Entities with graphQL disabled cannot be used in a relationship with another entity.
        /// 3. If the LinkingSourceFields or sourceFields and LinkingTargetFields or targetFields are not
        /// specified in the config for the given linkingObject, then the underlying database should
        /// contain a foreign key relationship between the source and target entity.
        /// 4. If linkingObject is null, and either of SourceFields or targetFields is null, then foreignKey pair
        /// between source and target entity must be defined in the DB.
        /// </summary>
        /// <exception cref="DataApiBuilderException">Throws exception whenever some validation fails.</exception>
        public void ValidateRelationshipsInConfig(RuntimeConfig runtimeConfig, ISqlMetadataProvider sqlMetadataProvider)
        {
            _logger.LogInformation("Validating Relationship Section in Config...");

            // Loop through each entity in the config and verify its relationship.
            foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
            {
                // Skipping relationship validation if entity has no relationship
                // or if graphQL is disabled.
                if (entity.Relationships is null || false.Equals(entity.GraphQL))
                {
                    continue;
                }

                if (entity.ObjectType is not SourceType.Table && entity.Relationships is not null
                    && entity.Relationships.Count > 0)
                {
                    throw new DataApiBuilderException(
                            message: $"Cannot define relationship for entity: {entity}",
                            statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                }

                foreach ((string relationshipName, Relationship relationship) in entity.Relationships!)
                {
                    // Validate if entity referenced in relationship is defined in the config.
                    if (!runtimeConfig.Entities.ContainsKey(relationship.TargetEntity))
                    {
                        throw new DataApiBuilderException(
                            message: $"entity: {relationship.TargetEntity} used for relationship is not defined in the config.",
                            statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                    }

                    // Validation to ensure that an entity with graphQL disabled cannot be referenced in a relationship by other entities
                    object? targetEntityGraphQLDetails = runtimeConfig.Entities[relationship.TargetEntity].GraphQL;
                    if (false.Equals(targetEntityGraphQLDetails))
                    {
                        throw new DataApiBuilderException(
                            message: $"entity: {relationship.TargetEntity} is disabled for GraphQL.",
                            statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                    }

                    DatabaseTable sourceDatabaseObject = (DatabaseTable)sqlMetadataProvider.EntityToDatabaseObject[entityName];
                    DatabaseTable targetDatabaseObject = (DatabaseTable)sqlMetadataProvider.EntityToDatabaseObject[relationship.TargetEntity];
                    if (relationship.LinkingObject is not null)
                    {
                        (string linkingTableSchema, string linkingTableName) = sqlMetadataProvider.ParseSchemaAndDbTableName(relationship.LinkingObject)!;
                        DatabaseTable linkingDatabaseObject = new(linkingTableSchema, linkingTableName);

                        if (relationship.LinkingSourceFields is null || relationship.SourceFields is null)
                        {
                            if (!sqlMetadataProvider.VerifyForeignKeyExistsInDB(linkingDatabaseObject, sourceDatabaseObject))
                            {
                                throw new DataApiBuilderException(
                                message: $"Could not find relationship between Linking Object: {relationship.LinkingObject}" +
                                    $" and entity: {entityName}.",
                                statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                            }
                        }

                        if (relationship.LinkingTargetFields is null || relationship.TargetFields is null)
                        {
                            if (!sqlMetadataProvider.VerifyForeignKeyExistsInDB(linkingDatabaseObject, targetDatabaseObject))
                            {
                                throw new DataApiBuilderException(
                                message: $"Could not find relationship between Linking Object: {relationship.LinkingObject}" +
                                    $" and entity: {relationship.TargetEntity}.",
                                statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
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
                            _logger.LogDebug($"{entityName}: {sourceDBOName}({referencedSourceColumns}) related to {cardinality} " +
                                $"{relationship.TargetEntity}: {targetDBOName}({referencedTargetColumns}) by " +
                                $"{relationship.LinkingObject}(linking.source.fields: {referencingSourceColumns}), (linking.target.fields: {referencingTargetColumns})");
                        }
                    }

                    if (relationship.LinkingObject is null
                        && (relationship.SourceFields is null || relationship.TargetFields is null))
                    {
                        if (!sqlMetadataProvider.VerifyForeignKeyExistsInDB(sourceDatabaseObject, targetDatabaseObject))
                        {
                            throw new DataApiBuilderException(
                            message: $"Could not find relationship between entities: {entityName} and {relationship.TargetEntity}.",
                            statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
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
                        _logger.LogDebug($"{entityName}: {sourceDBOName}({sourceColumns}) is related to {cardinality} " +
                            $"{relationship.TargetEntity}: {targetDBOName}({targetColumns}).");
                    }
                }
            }
        }

        /// <summary>
        /// Validates the parameters given in the config are consistent with the DB i.e., config has all
        /// the parameters that are specified for the stored procedure in DB.
        /// </summary>
        public void ValidateStoredProceduresInConfig(RuntimeConfig runtimeConfig, ISqlMetadataProvider sqlMetadataProvider)
        {
            foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
            {
                // We are only doing this pre-check for GraphQL because for GraphQL we need the correct schema while making request
                // so if the schema is not correct we will halt the engine
                // but for rest we can do it when a request is made and only fail that particular request.
                entity.TryPopulateSourceFields();
                if (entity.ObjectType is SourceType.StoredProcedure &&
                    entity.GraphQL is not null && !(entity.GraphQL is bool graphQLEnabled && !graphQLEnabled))
                {
                    DatabaseObject dbObject = sqlMetadataProvider.EntityToDatabaseObject[entityName];
                    StoredProcedureRequestContext sqRequestContext = new(
                                                                            entityName,
                                                                            dbObject,
                                                                            JsonSerializer.SerializeToElement(entity.Parameters),
                                                                            Config.Operation.All);
                    try
                    {
                        RequestValidator.ValidateStoredProcedureRequestContext(sqRequestContext, sqlMetadataProvider);
                    }
                    catch (DataApiBuilderException e)
                    {
                        throw new DataApiBuilderException(
                            message: e.Message,
                            statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                    }
                }
            }
        }

        /// <summary>
        /// Pre-processes the permissions section of the runtime config object.
        /// For eg. removing the @item. directives, checking for invalid characters in claimTypes etc.
        /// </summary>
        /// <param name="runtimeConfig">The deserialised config object obtained from the json config supplied.</param>
        public void ProcessPermissionsInConfig(RuntimeConfig runtimeConfig)
        {
            foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
            {
                foreach (PermissionSetting permissionSetting in entity.Permissions)
                {
                    Object[] actions = permissionSetting.Operations;

                    // processedActions will contain the processed actions which are formed after performing all kind of
                    // validations and pre-processing.
                    List<Object> processedActions = new();
                    foreach (Object action in actions)
                    {
                        if (((JsonElement)action).ValueKind == JsonValueKind.String)
                        {
                            processedActions.Add(action);
                        }
                        else
                        {
                            PermissionOperation configOperation;
                            configOperation = JsonSerializer.Deserialize<Config.PermissionOperation>(action.ToString()!)!;

                            if (configOperation.Policy is not null && configOperation.Policy.Database is not null)
                            {
                                // Remove all the occurrences of @item. directive from the policy.
                                configOperation.Policy.Database = ProcessFieldsInPolicy(configOperation.Policy.Database);
                            }

                            processedActions.Add(JsonSerializer.SerializeToElement(configOperation));
                        }
                    }

                    // Update the permissionSetting.Actions to point to the processedActions.
                    permissionSetting.Operations = processedActions.ToArray();
                }
            }
        }

        /// <summary>
        /// Helper method which takes in the database policy and returns the processed policy
        /// without @item. directives before field names.
        /// </summary>
        /// <param name="policy">Raw database policy</param>
        /// <returns>Processed policy without @item. directives before field names.</returns>
        private static string ProcessFieldsInPolicy(string policy)
        {
            string fieldCharsRgx = @"@item\.[a-zA-Z0-9_]*";

            // processedPolicy would be devoid of @item. directives.
            string processedPolicy = Regex.Replace(policy, fieldCharsRgx, (columnNameMatch) =>
            columnNameMatch.Value.Substring(AuthorizationResolver.FIELD_PREFIX.Length));
            return processedPolicy;
        }

        /// <summary>
        /// Method to do different validations on claims in the policy.
        /// </summary>
        /// <param name="policy">The policy to be validated and processed.</param>
        /// <returns>Processed policy</returns>
        /// <exception cref="DataApiBuilderException">Throws exception when one or the other validations fail.</exception>
        private void ValidateClaimsInPolicy(string policy)
        {
            // Find all the claimTypes from the policy
            MatchCollection claimTypes = GetClaimTypesInPolicy(policy);
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetRuntimeConfiguration();
            bool isStaticWebAppsAuthConfigured = Enum.TryParse<EasyAuthType>(runtimeConfig.AuthNConfig!.Provider, ignoreCase: true, out EasyAuthType easyAuthMode) ?
                easyAuthMode is EasyAuthType.StaticWebApps : false;

            foreach (Match claimType in claimTypes)
            {
                // Remove the prefix @claims. from the claimType
                string typeOfClaim = claimType.Value.Substring(AuthorizationResolver.CLAIM_PREFIX.Length);

                if (string.IsNullOrWhiteSpace(typeOfClaim))
                {
                    // Empty claimType is not allowed
                    throw new DataApiBuilderException(
                        message: $"Claimtype cannot be empty.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError
                        );
                }

                if (_invalidClaimCharsRgx.IsMatch(typeOfClaim))
                {
                    // Not a valid claimType containing allowed characters
                    throw new DataApiBuilderException(
                        message: $"Invalid format for claim type {typeOfClaim} supplied in policy.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
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
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError
                        );
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
        private static void AreFieldsAccessible(string policy, HashSet<string>? includedFields, HashSet<string> excludedFields)
        {
            // Pattern of field references in the policy
            string fieldCharsRgx = @"@item\.[a-zA-Z0-9_]*";
            MatchCollection fieldNameMatches = Regex.Matches(policy, fieldCharsRgx);

            foreach (Match fieldNameMatch in fieldNameMatches)
            {
                if (!IsFieldAccessible(fieldNameMatch, includedFields, excludedFields))
                {
                    throw new DataApiBuilderException(
                    message: $"Not all the columns required by policy are accessible.",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
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
                statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
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
        public static bool IsValidPermissionAction(Config.Operation action, Entity entity, string entityName)
        {
            if (entity.ObjectType is SourceType.StoredProcedure)
            {
                if (action is not Config.Operation.All && !PermissionOperation.ValidStoredProcedurePermissionOperations.Contains(action))
                {
                    throw new DataApiBuilderException(
                        message: $"Invalid operation for Entity: {entityName}. " +
                            $"Stored procedures can only be configured with the 'execute' operation.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                }

                return true;
            }
            else
            {
                if (action is Config.Operation.Execute)
                {
                    throw new DataApiBuilderException(
                        message: $"Invalid operation for Entity: {entityName}. " +
                            $"The 'execute' operation can only be configured for entities backed by stored procedures.",
                        statusCode: HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                }

                return action is Config.Operation.All || PermissionOperation.ValidPermissionOperations.Contains(action);
            }
        }
    }
}
