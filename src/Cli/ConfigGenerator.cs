// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.NamingPolicies;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service;
using Cli.Commands;
using Microsoft.Extensions.Logging;
using static Cli.Utils;

namespace Cli
{
    /// <summary>
    /// Contains the methods for Initializing the config file and Adding/Updating Entities.
    /// </summary>
    public class ConfigGenerator
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private static ILogger<ConfigGenerator> _logger;
#pragma warning restore CS8618

        public static void SetLoggerForCliConfigGenerator(
            ILogger<ConfigGenerator> configGeneratorLoggerFactory)
        {
            _logger = configGeneratorLoggerFactory;
        }

        /// <summary>
        /// This method will generate the initial config with databaseType and connection-string.
        /// </summary>
        public static bool TryGenerateConfig(InitOptions options, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            string runtimeConfigFile = FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME;
            if (!string.IsNullOrWhiteSpace(options.Config))
            {
                _logger.LogInformation("Generating user provided config file with name: {configFileName}", options.Config);
                runtimeConfigFile = options.Config;
            }
            else
            {
                string? environmentValue = Environment.GetEnvironmentVariable(FileSystemRuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME);
                if (!string.IsNullOrWhiteSpace(environmentValue))
                {
                    _logger.LogInformation("The environment variable {variableName} has a value of {variableValue}", FileSystemRuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
                    runtimeConfigFile = FileSystemRuntimeConfigLoader.GetEnvironmentFileName(FileSystemRuntimeConfigLoader.CONFIGFILE_NAME, environmentValue);
                    _logger.LogInformation("Generating environment config file: {configPath}", fileSystem.Path.GetFullPath(runtimeConfigFile));
                }
                else
                {
                    _logger.LogInformation("Generating default config file: {config}", fileSystem.Path.GetFullPath(runtimeConfigFile));
                }
            }

            // File existence checked to avoid overwriting the existing configuration.
            if (fileSystem.File.Exists(runtimeConfigFile))
            {
                _logger.LogError("Config file: {runtimeConfigFile} already exists. Please provide a different name or remove the existing config file.",
                    fileSystem.Path.GetFullPath(runtimeConfigFile));
                return false;
            }

            // Creating a new json file with runtime configuration
            if (!TryCreateRuntimeConfig(options, loader, fileSystem, out RuntimeConfig? runtimeConfig))
            {
                return false;
            }

            return WriteRuntimeConfigToFile(runtimeConfigFile, runtimeConfig, fileSystem);
        }

        /// <summary>
        /// Create a runtime config json string.
        /// </summary>
        /// <param name="options">Init options</param>
        /// <param name="runtimeConfig">Output runtime config json.</param>
        /// <returns>True on success. False otherwise.</returns>
        public static bool TryCreateRuntimeConfig(InitOptions options, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem, [NotNullWhen(true)] out RuntimeConfig? runtimeConfig)
        {
            runtimeConfig = null;

            DatabaseType dbType = options.DatabaseType;
            string? restPath = options.RestPath;
            string graphQLPath = options.GraphQLPath;
            string? runtimeBaseRoute = options.RuntimeBaseRoute;
            Dictionary<string, JsonElement> dbOptions = new();

            HyphenatedNamingPolicy namingPolicy = new();

            // If --rest.disabled flag is included in the init command, we log a warning to not use this flag as it will be deprecated in future versions of DAB.
            if (options.RestDisabled is true)
            {
                _logger.LogWarning("The option --rest.disabled will be deprecated and support for the option will be removed in future versions of Data API builder." +
                    " We recommend that you use the --rest.enabled option instead.");
            }

            // If --graphql.disabled flag is included in the init command, we log a warning to not use this flag as it will be deprecated in future versions of DAB.
            if (options.GraphQLDisabled is true)
            {
                _logger.LogWarning("The option --graphql.disabled will be deprecated and support for the option will be removed in future versions of Data API builder." +
                    " We recommend that you use the --graphql.enabled option instead.");
            }

            bool restEnabled, graphQLEnabled;
            if (!TryDetermineIfApiIsEnabled(options.RestDisabled, options.RestEnabled, ApiType.REST, out restEnabled) ||
                !TryDetermineIfApiIsEnabled(options.GraphQLDisabled, options.GraphQLEnabled, ApiType.GraphQL, out graphQLEnabled))
            {
                return false;
            }

            switch (dbType)
            {
                case DatabaseType.CosmosDB_NoSQL:
                    // If cosmosdb_nosql is specified, rest is disabled.
                    restEnabled = false;

                    string? cosmosDatabase = options.CosmosNoSqlDatabase;
                    string? cosmosContainer = options.CosmosNoSqlContainer;
                    string? graphQLSchemaPath = options.GraphQLSchemaPath;
                    if (string.IsNullOrEmpty(cosmosDatabase) || string.IsNullOrEmpty(graphQLSchemaPath))
                    {
                        _logger.LogError("Missing mandatory configuration option for CosmosDB_NoSql: --cosmosdb_nosql-database, and --graphql-schema");
                        return false;
                    }

                    if (!fileSystem.File.Exists(graphQLSchemaPath))
                    {
                        _logger.LogError("GraphQL Schema File: {graphQLSchemaPath} not found.", graphQLSchemaPath);
                        return false;
                    }

                    // If the option --rest.path is specified for cosmosdb_nosql, log a warning because
                    // rest is not supported for cosmosdb_nosql yet.
                    if (!RestRuntimeOptions.DEFAULT_PATH.Equals(restPath))
                    {
                        _logger.LogWarning("Configuration option --rest.path is not honored for cosmosdb_nosql since CosmosDB does not support REST.");
                    }

                    if (options.RestRequestBodyStrict is not CliBoolean.None)
                    {
                        _logger.LogWarning("Configuration option --rest.request-body-strict is not honored for cosmosdb_nosql since CosmosDB does not support REST.");
                    }

                    restPath = null;
                    dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database)), JsonSerializer.SerializeToElement(cosmosDatabase));
                    dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Container)), JsonSerializer.SerializeToElement(cosmosContainer));
                    dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Schema)), JsonSerializer.SerializeToElement(graphQLSchemaPath));
                    break;

                case DatabaseType.DWSQL:
                case DatabaseType.MSSQL:
                    dbOptions.Add(namingPolicy.ConvertName(nameof(MsSqlOptions.SetSessionContext)), JsonSerializer.SerializeToElement(options.SetSessionContext));

                    break;
                case DatabaseType.MySQL:
                case DatabaseType.PostgreSQL:
                case DatabaseType.CosmosDB_PostgreSQL:
                    break;
                default:
                    throw new Exception($"DatabaseType: ${dbType} not supported.Please provide a valid database-type.");
            }

            // default value of connection-string should be used, i.e Empty-string
            // if not explicitly provided by the user
            DataSource dataSource = new(dbType, options.ConnectionString ?? string.Empty, dbOptions);

            if (!ValidateAudienceAndIssuerForJwtProvider(options.AuthenticationProvider, options.Audience, options.Issuer))
            {
                return false;
            }

            if (!IsURIComponentValid(restPath))
            {
                _logger.LogError("{apiType} path {message}", ApiType.REST, RuntimeConfigValidator.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG);
                return false;
            }

            if (!IsURIComponentValid(options.GraphQLPath))
            {
                _logger.LogError("{apiType} path {message}", ApiType.GraphQL, RuntimeConfigValidator.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG);
                return false;
            }

            if (!IsURIComponentValid(runtimeBaseRoute))
            {
                _logger.LogError("Runtime base-route {message}", RuntimeConfigValidator.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG);
                return false;
            }

            if (runtimeBaseRoute is not null)
            {
                if (!Enum.TryParse(options.AuthenticationProvider, ignoreCase: true, out EasyAuthType easyAuthMode) || easyAuthMode is not EasyAuthType.StaticWebApps)
                {
                    _logger.LogError("Runtime base-route can only be specified when the authentication provider is Static Web Apps.");
                    return false;
                }
            }

            if (options.RestDisabled && options.GraphQLDisabled)
            {
                _logger.LogError("Both Rest and GraphQL cannot be disabled together.");
                return false;
            }

            string dabSchemaLink = loader.GetPublishedDraftSchemaLink();

            // Prefix REST path with '/', if not already present.
            if (restPath is not null && !restPath.StartsWith('/'))
            {
                restPath = "/" + restPath;
            }

            // Prefix base-route with '/', if not already present.
            if (runtimeBaseRoute is not null && !runtimeBaseRoute.StartsWith('/'))
            {
                runtimeBaseRoute = "/" + runtimeBaseRoute;
            }

            // Prefix GraphQL path with '/', if not already present.
            if (!graphQLPath.StartsWith('/'))
            {
                graphQLPath = "/" + graphQLPath;
            }

            runtimeConfig = new(
                Schema: dabSchemaLink,
                DataSource: dataSource,
                Runtime: new(
                    Rest: new(restEnabled, restPath ?? RestRuntimeOptions.DEFAULT_PATH, options.RestRequestBodyStrict is CliBoolean.False ? false : true),
                    GraphQL: new(graphQLEnabled, graphQLPath),
                    Host: new(
                        Cors: new(options.CorsOrigin?.ToArray() ?? Array.Empty<string>()),
                        Authentication: new(
                            Provider: options.AuthenticationProvider,
                            Jwt: (options.Audience is null && options.Issuer is null) ? null : new(options.Audience, options.Issuer)),
                        Mode: options.HostMode),
                    BaseRoute: runtimeBaseRoute
                ),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));

            return true;
        }

        /// <summary>
        /// Helper method to determine if the api is enabled or not based on the enabled/disabled options in the dab init command.
        /// The method also validates that there is no mismatch in semantics of enabling/disabling the REST/GraphQL API(s)
        /// based on the values supplied in the enabled/disabled options for the API in the init command.
        /// </summary>
        /// <param name="apiDisabledOptionValue">Value of disabled option as in the init command. If the option is omitted in the command, default value is assigned.</param>
        /// <param name="apiEnabledOptionValue">Value of enabled option as in the init command. If the option is omitted in the command, default value is assigned.</param>
        /// <param name="apiType">ApiType - REST/GraphQL.</param>
        /// <param name="isApiEnabled">Boolean value indicating whether the API endpoint is enabled or not.</param>
        private static bool TryDetermineIfApiIsEnabled(bool apiDisabledOptionValue, CliBool apiEnabledOptionValue, ApiType apiType, out bool isApiEnabled)
        {
            if (!apiDisabledOptionValue)
            {
                isApiEnabled = apiEnabledOptionValue == CliBool.False ? false : true;
                // This indicates that the --api.disabled option was not included in the init command.
                // In such a case, we honor the --api.enabled option.
                return true;
            }

            if (apiEnabledOptionValue is CliBool.None)
            {
                // This means that the --api.enabled option was not included in the init command.
                isApiEnabled = !apiDisabledOptionValue;
                return true;
            }

            // We hit this code only when both --api.enabled and --api.disabled flags are included in the init command.
            isApiEnabled = bool.Parse(apiEnabledOptionValue.ToString());
            if (apiDisabledOptionValue == isApiEnabled)
            {
                string apiName = apiType.ToString().ToLower();
                _logger.LogError($"Config generation failed due to mismatch in the semantics of enabling {apiType} API via " +
                    $"--{apiName}.disabled and --{apiName}.enabled options");
                return false;
            }

            return true;
        }

        /// <summary>
        /// This method will add a new Entity with the given REST and GraphQL endpoints, source, and permissions.
        /// It also supports fields that needs to be included or excluded for a given role and operation.
        /// </summary>
        public static bool TryAddEntityToConfigWithOptions(AddOptions options, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            if (!TryGetConfigFileBasedOnCliPrecedence(loader, options.Config, out string runtimeConfigFile))
            {
                return false;
            }

            if (!loader.TryLoadConfig(runtimeConfigFile, out RuntimeConfig? runtimeConfig))
            {
                _logger.LogError("Failed to read the config file: {runtimeConfigFile}.", runtimeConfigFile);
                return false;
            }

            if (!TryAddNewEntity(options, runtimeConfig, out RuntimeConfig updatedRuntimeConfig))
            {
                _logger.LogError("Failed to add a new entity.");
                return false;
            }

            return WriteRuntimeConfigToFile(runtimeConfigFile, updatedRuntimeConfig, fileSystem);
        }

        /// <summary>
        /// Add new entity to runtime config. This method will take the existing runtime config and add a new entity to it
        /// and return a new instance of the runtime config.
        /// </summary>
        /// <param name="options">AddOptions.</param>
        /// <param name="initialRuntimeConfig">The current instance of the <c>RuntimeConfig</c> that will be updated.</param>
        /// <param name="updatedRuntimeConfig">The updated instance of the <c>RuntimeConfig</c>.</param>
        /// <returns>True on success. False otherwise.</returns>
        public static bool TryAddNewEntity(AddOptions options, RuntimeConfig initialRuntimeConfig, out RuntimeConfig updatedRuntimeConfig)
        {
            updatedRuntimeConfig = initialRuntimeConfig;
            // If entity exists, we cannot add. Display warning
            //
            if (initialRuntimeConfig.Entities.ContainsKey(options.Entity))
            {
                _logger.LogWarning("Entity '{entityName}' is already present. No new changes are added to Config.", options.Entity);
                return false;
            }

            // Try to get the source object as string or DatabaseObjectSource for new Entity
            if (!TryCreateSourceObjectForNewEntity(
                options,
                initialRuntimeConfig.DataSource.DatabaseType == DatabaseType.CosmosDB_NoSQL,
                out EntitySource? source))
            {
                _logger.LogError("Unable to create the source object.");
                return false;
            }

            EntityActionPolicy? policy = GetPolicyForOperation(options.PolicyRequest, options.PolicyDatabase);
            EntityActionFields? field = GetFieldsForOperation(options.FieldsToInclude, options.FieldsToExclude);

            EntityPermission[]? permissionSettings = ParsePermission(options.Permissions, policy, field, source.Type);
            if (permissionSettings is null)
            {
                _logger.LogError("Please add permission in the following format. --permissions \"<<role>>:<<actions>>\"");
                return false;
            }

            bool isStoredProcedure = IsStoredProcedure(options);
            // Validations to ensure that REST methods and GraphQL operations can be configured only
            // for stored procedures
            if (options.GraphQLOperationForStoredProcedure is not null && !isStoredProcedure)
            {
                _logger.LogError("--graphql.operation can be configured only for stored procedures.");
                return false;
            }

            if ((options.RestMethodsForStoredProcedure is not null && options.RestMethodsForStoredProcedure.Any())
                && !isStoredProcedure)
            {
                _logger.LogError("--rest.methods can be configured only for stored procedures.");
                return false;
            }

            GraphQLOperation? graphQLOperationsForStoredProcedures = null;
            SupportedHttpVerb[]? SupportedRestMethods = null;
            if (isStoredProcedure)
            {
                if (CheckConflictingGraphQLConfigurationForStoredProcedures(options))
                {
                    _logger.LogError("Conflicting GraphQL configurations found.");
                    return false;
                }

                if (!TryAddGraphQLOperationForStoredProcedure(options, out graphQLOperationsForStoredProcedures))
                {
                    return false;
                }

                if (CheckConflictingRestConfigurationForStoredProcedures(options))
                {
                    _logger.LogError("Conflicting Rest configurations found.");
                    return false;
                }

                if (!TryAddSupportedRestMethodsForStoredProcedure(options, out SupportedRestMethods))
                {
                    return false;
                }
            }

            EntityRestOptions restOptions = ConstructRestOptions(options.RestRoute, SupportedRestMethods, initialRuntimeConfig.DataSource.DatabaseType == DatabaseType.CosmosDB_NoSQL);
            EntityGraphQLOptions graphqlOptions = ConstructGraphQLTypeDetails(options.GraphQLType, graphQLOperationsForStoredProcedures);

            // Create new entity.
            Entity entity = new(
                Source: source,
                Rest: restOptions,
                GraphQL: graphqlOptions,
                Permissions: permissionSettings,
                Relationships: null,
                Mappings: null);

            // Add entity to existing runtime config.
            IDictionary<string, Entity> entities = new Dictionary<string, Entity>(initialRuntimeConfig.Entities.Entities)
            {
                { options.Entity, entity }
            };
            updatedRuntimeConfig = initialRuntimeConfig with { Entities = new(new ReadOnlyDictionary<string, Entity>(entities)) };
            return true;
        }

        /// <summary>
        /// This method creates the source object for a new entity
        /// if the given source fields specified by the user are valid.
        /// </summary>
        public static bool TryCreateSourceObjectForNewEntity(
            AddOptions options,
            bool isCosmosDbNoSQL,
            [NotNullWhen(true)] out EntitySource? sourceObject)
        {
            sourceObject = null;

            // default entity type will be null if it's CosmosDB_NoSQL otherwise it will be Table
            EntitySourceType? objectType = isCosmosDbNoSQL ? null : EntitySourceType.Table;

            if (options.SourceType is not null)
            {
                // Try to Parse the SourceType
                if (!EnumExtensions.TryDeserialize(options.SourceType, out EntitySourceType? et))
                {
                    _logger.LogError(EnumExtensions.GenerateMessageForInvalidInput<EntitySourceType>(options.SourceType));
                    return false;
                }

                objectType = (EntitySourceType)et;
            }

            // Verify that parameter is provided with stored-procedure only
            // and key fields with table/views.
            if (!VerifyCorrectPairingOfParameterAndKeyFieldsWithType(
                    objectType,
                    options.SourceParameters,
                    options.SourceKeyFields))
            {
                return false;
            }

            // Parses the string array to parameter Dictionary
            if (!TryParseSourceParameterDictionary(
                    options.SourceParameters,
                    out Dictionary<string, object>? parametersDictionary))
            {
                return false;
            }

            string[]? sourceKeyFields = null;
            if (options.SourceKeyFields is not null && options.SourceKeyFields.Any())
            {
                sourceKeyFields = options.SourceKeyFields.ToArray();
            }

            // Try to get the source object as string or DatabaseObjectSource
            if (!TryCreateSourceObject(
                    options.Source,
                    objectType,
                    parametersDictionary,
                    sourceKeyFields,
                    out sourceObject))
            {
                _logger.LogError("Unable to parse the given source.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Parse permission string to create PermissionSetting array.
        /// </summary>
        /// <param name="permissions">Permission input string as IEnumerable.</param>
        /// <param name="policy">policy to add for this permission.</param>
        /// <param name="fields">fields to include and exclude for this permission.</param>
        /// <param name="sourceType">type of source object.</param>
        /// <returns></returns>
        public static EntityPermission[]? ParsePermission(
            IEnumerable<string> permissions,
            EntityActionPolicy? policy,
            EntityActionFields? fields,
            EntitySourceType? sourceType)
        {
            // Getting Role and Operations from permission string
            string? role, operations;
            if (!TryGetRoleAndOperationFromPermission(permissions, out role, out operations))
            {
                _logger.LogError("Failed to fetch the role and operation from the given permission string: {permissions}.", string.Join(SEPARATOR, permissions));
                return null;
            }

            // Check if provided operations are valid
            if (!VerifyOperations(operations!.Split(","), sourceType))
            {
                return null;
            }

            EntityPermission[] permissionSettings = new[]
            {
                CreatePermissions(role!, operations!, policy, fields)
            };

            return permissionSettings;
        }

        /// <summary>
        /// This method will update an existing Entity with the given REST and GraphQL endpoints, source, and permissions.
        /// It also supports updating fields that need to be included or excluded for a given role and operation.
        /// </summary>
        public static bool TryUpdateEntityWithOptions(UpdateOptions options, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            if (!TryGetConfigFileBasedOnCliPrecedence(loader, options.Config, out string runtimeConfigFile))
            {
                return false;
            }

            if (!loader.TryLoadConfig(runtimeConfigFile, out RuntimeConfig? runtimeConfig))
            {
                _logger.LogError("Failed to read the config file: {runtimeConfigFile}.", runtimeConfigFile);
                return false;
            }

            if (!TryUpdateExistingEntity(options, runtimeConfig, out RuntimeConfig updatedConfig))
            {
                _logger.LogError("Failed to update the Entity: {entityName}.", options.Entity);
                return false;
            }

            return WriteRuntimeConfigToFile(runtimeConfigFile, updatedConfig, fileSystem);
        }

        /// <summary>
        /// Update an existing entity in the runtime config. This method will receive the existing runtime config
        /// and update the entity before returning a new instance of the runtime config.
        /// </summary>
        /// <param name="options">UpdateOptions.</param>
        /// <param name="initialConfig">The initial <c>RuntimeConfig</c>.</param>
        /// <param name="updatedConfig">The updated <c>RuntimeConfig</c>.</param>
        /// <returns>True on success. False otherwise.</returns>
        public static bool TryUpdateExistingEntity(UpdateOptions options, RuntimeConfig initialConfig, out RuntimeConfig updatedConfig)
        {
            updatedConfig = initialConfig;
            // Check if Entity is present
            if (!initialConfig.Entities.TryGetValue(options.Entity, out Entity? entity))
            {
                _logger.LogError("Entity: '{entityName}' not found. Please add the entity first.", options.Entity);
                return false;
            }

            if (!TryGetUpdatedSourceObjectWithOptions(options, entity, out EntitySource? updatedSource))
            {
                _logger.LogError("Failed to update the source object.");
                return false;
            }

            bool isCurrentEntityStoredProcedure = IsStoredProcedure(entity);
            bool doOptionsRepresentStoredProcedure = options.SourceType is not null && IsStoredProcedure(options);

            // Validations to ensure that REST methods and GraphQL operations can be configured only
            // for stored procedures
            if (options.GraphQLOperationForStoredProcedure is not null &&
                !(isCurrentEntityStoredProcedure || doOptionsRepresentStoredProcedure))
            {
                _logger.LogError("--graphql.operation can be configured only for stored procedures.");
                return false;
            }

            if ((options.RestMethodsForStoredProcedure is not null && options.RestMethodsForStoredProcedure.Any())
                && !(isCurrentEntityStoredProcedure || doOptionsRepresentStoredProcedure))
            {
                _logger.LogError("--rest.methods can be configured only for stored procedures.");
                return false;
            }

            if (isCurrentEntityStoredProcedure || doOptionsRepresentStoredProcedure)
            {
                if (CheckConflictingGraphQLConfigurationForStoredProcedures(options))
                {
                    _logger.LogError("Conflicting GraphQL configurations found.");
                    return false;
                }

                if (CheckConflictingRestConfigurationForStoredProcedures(options))
                {
                    _logger.LogError("Conflicting Rest configurations found.");
                    return false;
                }
            }

            EntityRestOptions updatedRestDetails = ConstructUpdatedRestDetails(entity, options, initialConfig.DataSource.DatabaseType == DatabaseType.CosmosDB_NoSQL);
            EntityGraphQLOptions updatedGraphQLDetails = ConstructUpdatedGraphQLDetails(entity, options);
            EntityPermission[]? updatedPermissions = entity!.Permissions;
            Dictionary<string, EntityRelationship>? updatedRelationships = entity.Relationships;
            Dictionary<string, string>? updatedMappings = entity.Mappings;
            EntityActionPolicy? updatedPolicy = GetPolicyForOperation(options.PolicyRequest, options.PolicyDatabase);
            EntityActionFields? updatedFields = GetFieldsForOperation(options.FieldsToInclude, options.FieldsToExclude);

            if (!updatedGraphQLDetails.Enabled)
            {
                _logger.LogWarning("Disabling GraphQL for this entity will restrict its usage in relationships");
            }

            EntitySourceType? updatedSourceType = updatedSource.Type;

            if (options.Permissions is not null && options.Permissions.Any())
            {
                // Get the Updated Permission Settings
                updatedPermissions = GetUpdatedPermissionSettings(entity, options.Permissions, updatedPolicy, updatedFields, updatedSourceType);

                if (updatedPermissions is null)
                {
                    _logger.LogError("Failed to update permissions.");
                    return false;
                }
            }
            else
            {

                if (options.FieldsToInclude is not null && options.FieldsToInclude.Any()
                    || options.FieldsToExclude is not null && options.FieldsToExclude.Any())
                {
                    _logger.LogInformation("--permissions is mandatory with --fields.include and --fields.exclude.");
                    return false;
                }

                if (options.PolicyRequest is not null || options.PolicyDatabase is not null)
                {
                    _logger.LogInformation("--permissions is mandatory with --policy-request and --policy-database.");
                    return false;
                }

                if (updatedSourceType is EntitySourceType.StoredProcedure &&
                    !VerifyPermissionOperationsForStoredProcedures(entity.Permissions))
                {
                    return false;
                }
            }

            if (options.Relationship is not null)
            {
                if (!VerifyCanUpdateRelationship(initialConfig, options.Cardinality, options.TargetEntity))
                {
                    return false;
                }

                if (updatedRelationships is null)
                {
                    updatedRelationships = new();
                }

                EntityRelationship? new_relationship = CreateNewRelationshipWithUpdateOptions(options);
                if (new_relationship is null)
                {
                    return false;
                }

                updatedRelationships[options.Relationship] = new_relationship;
            }

            if (options.Map is not null && options.Map.Any())
            {
                // Parsing mappings dictionary from Collection
                if (!TryParseMappingDictionary(options.Map, out updatedMappings))
                {
                    return false;
                }
            }

            Entity updatedEntity = new(
                Source: updatedSource,
                Rest: updatedRestDetails,
                GraphQL: updatedGraphQLDetails,
                Permissions: updatedPermissions,
                Relationships: updatedRelationships,
                Mappings: updatedMappings);
            IDictionary<string, Entity> entities = new Dictionary<string, Entity>(initialConfig.Entities.Entities)
            {
                [options.Entity] = updatedEntity
            };
            updatedConfig = initialConfig with { Entities = new(new ReadOnlyDictionary<string, Entity>(entities)) };
            return true;
        }

        /// <summary>
        /// Get an array of PermissionSetting by merging the existing permissions of an entity with new permissions.
        /// If a role has existing permission and user updates permission of that role,
        /// the old permission will be overwritten. Otherwise, a new permission of the role will be added.
        /// </summary>
        /// <param name="entityToUpdate">entity whose permission needs to be updated</param>
        /// <param name="permissions">New permission to be applied.</param>
        /// <param name="policy">policy to added for this permission</param>
        /// <param name="fields">fields to be included and excluded from the operation permission.</param>
        /// <param name="sourceType">Type of Source object.</param>
        /// <returns> On failure, returns null. Else updated PermissionSettings array will be returned.</returns>
        private static EntityPermission[]? GetUpdatedPermissionSettings(Entity entityToUpdate,
                                                                        IEnumerable<string> permissions,
                                                                        EntityActionPolicy? policy,
                                                                        EntityActionFields? fields,
                                                                        EntitySourceType? sourceType)
        {

            // Parse role and operations from the permissions string
            //
            if (!TryGetRoleAndOperationFromPermission(permissions, out string? newRole, out string? newOperations))
            {
                _logger.LogError("Failed to fetch the role and operation from the given permission string: {permissions}.", permissions);
                return null;
            }

            List<EntityPermission> updatedPermissionsList = new();
            string[] newOperationArray = newOperations.Split(",");

            // Verifies that the list of operations declared are valid for the specified sourceType.
            // Example: Stored-procedure can only have 1 operation.
            if (!VerifyOperations(newOperationArray, sourceType))
            {
                return null;
            }

            bool role_found = false;
            // Loop through the current permissions
            foreach (EntityPermission permission in entityToUpdate.Permissions)
            {
                // Find the role that needs to be updated
                if (permission.Role.Equals(newRole))
                {
                    role_found = true;
                    if (sourceType is EntitySourceType.StoredProcedure)
                    {
                        // Since, Stored-Procedures can have only 1 CRUD action. So, when update is requested with new action, we simply replace it.
                        updatedPermissionsList.Add(CreatePermissions(newRole, newOperationArray.First(), policy: null, fields: null));
                    }
                    else if (newOperationArray.Length is 1 && WILDCARD.Equals(newOperationArray[0]))
                    {
                        // If the user inputs WILDCARD as operation, we overwrite the existing operations.
                        updatedPermissionsList.Add(CreatePermissions(newRole!, WILDCARD, policy, fields));
                    }
                    else
                    {
                        // User didn't use WILDCARD, and wants to update some of the operations.
                        IDictionary<EntityActionOperation, EntityAction> existingOperations = ConvertOperationArrayToIEnumerable(permission.Actions, entityToUpdate.Source.Type);

                        // Merge existing operations with new operations
                        EntityAction[] updatedOperationArray = GetUpdatedOperationArray(newOperationArray, policy, fields, existingOperations);

                        updatedPermissionsList.Add(new EntityPermission(newRole, updatedOperationArray));
                    }
                }
                else
                {
                    updatedPermissionsList.Add(permission);
                }
            }

            // If the role we are trying to update is not found, we create a new one
            // and add it to permissionSettings list.
            if (!role_found)
            {
                updatedPermissionsList.Add(CreatePermissions(newRole, newOperations, policy, fields));
            }

            return updatedPermissionsList.ToArray();
        }

        /// <summary>
        /// Merge old and new operations into a new list. Take all new updated operations.
        /// Only add existing operations to the merged list if there is no update.
        /// </summary>
        /// <param name="newOperations">operation items to update received from user.</param>
        /// <param name="fieldsToInclude">fields that are included for the operation permission</param>
        /// <param name="fieldsToExclude">fields that are excluded from the operation permission.</param>
        /// <param name="existingOperations">operation items present in the config.</param>
        /// <returns>Array of updated operation objects</returns>
        private static EntityAction[] GetUpdatedOperationArray(string[] newOperations,
                                                        EntityActionPolicy? newPolicy,
                                                        EntityActionFields? newFields,
                                                        IDictionary<EntityActionOperation, EntityAction> existingOperations)
        {
            Dictionary<EntityActionOperation, EntityAction> updatedOperations = new();

            EntityActionPolicy? existingPolicy = null;
            EntityActionFields? existingFields = null;

            // Adding the new operations in the updatedOperationList
            foreach (string operation in newOperations)
            {
                // Getting existing Policy and Fields
                if (EnumExtensions.TryDeserialize(operation, out EntityActionOperation? op))
                {
                    if (existingOperations.ContainsKey((EntityActionOperation)op))
                    {
                        existingPolicy = existingOperations[(EntityActionOperation)op].Policy;
                        existingFields = existingOperations[(EntityActionOperation)op].Fields;
                    }

                    // Checking if Policy and Field update is required
                    EntityActionPolicy? updatedPolicy = newPolicy is null ? existingPolicy : newPolicy;
                    EntityActionFields? updatedFields = newFields is null ? existingFields : newFields;

                    updatedOperations.Add((EntityActionOperation)op, new EntityAction((EntityActionOperation)op, updatedFields, updatedPolicy));
                }
            }

            // Looping through existing operations
            foreach ((EntityActionOperation op, EntityAction act) in existingOperations)
            {
                // If any existing operation doesn't require update, it is added as it is.
                if (!updatedOperations.ContainsKey(op))
                {
                    updatedOperations.Add(op, act);
                }
            }

            return updatedOperations.Values.ToArray();
        }

        /// <summary>
        /// Parses updated options and uses them to create a new sourceObject
        /// for the given entity.
        /// Verifies if the given combination of fields is valid for update
        /// and then it updates it, else it fails.
        /// </summary>
        private static bool TryGetUpdatedSourceObjectWithOptions(
            UpdateOptions options,
            Entity entity,
            [NotNullWhen(true)] out EntitySource? updatedSourceObject)
        {
            updatedSourceObject = null;
            string updatedSourceName = options.Source ?? entity.Source.Object;
            string[]? updatedKeyFields = entity.Source.KeyFields;
            EntitySourceType? updatedSourceType = entity.Source.Type;
            Dictionary<string, object>? updatedSourceParameters = entity.Source.Parameters;

            // If SourceType provided by user is null,
            // no update is required.
            if (options.SourceType is not null)
            {
                if (!EnumExtensions.TryDeserialize(options.SourceType, out EntitySourceType? deserializedEntityType))
                {
                    _logger.LogError(EnumExtensions.GenerateMessageForInvalidInput<EntitySourceType>(options.SourceType));
                    return false;
                }

                updatedSourceType = (EntitySourceType)deserializedEntityType;

                if (IsStoredProcedureConvertedToOtherTypes(entity, options) || IsEntityBeingConvertedToStoredProcedure(entity, options))
                {
                    _logger.LogWarning(
                        "Stored procedures can be configured only with '{storedProcedureAction}' action whereas tables/views are configured with CRUD actions. Update the actions configured for all the roles for this entity.",
                        EntityActionOperation.Execute);
                }
            }

            // No need to validate parameter and key field usage when there are no changes to the source object defined in 'options'
            if ((options.SourceType is not null
                || (options.SourceParameters is not null && options.SourceParameters.Any())
                || (options.SourceKeyFields is not null && options.SourceKeyFields.Any()))
                && !VerifyCorrectPairingOfParameterAndKeyFieldsWithType(
                    updatedSourceType,
                    options.SourceParameters,
                    options.SourceKeyFields))
            {
                return false;
            }

            // Changing source object from stored-procedure to table/view
            // should automatically update the parameters to be null.
            // Similarly from table/view to stored-procedure, key-fields
            // should be marked null.
            if (EntitySourceType.StoredProcedure.Equals(updatedSourceType))
            {
                updatedKeyFields = null;
            }
            else
            {
                updatedSourceParameters = null;
            }

            // If given SourceParameter is null or is Empty, no update is required.
            // Else updatedSourceParameters will contain the parsed dictionary of parameters.
            if (options.SourceParameters is not null && options.SourceParameters.Any() &&
                !TryParseSourceParameterDictionary(options.SourceParameters, out updatedSourceParameters))
            {
                return false;
            }

            if (options.SourceKeyFields is not null && options.SourceKeyFields.Any())
            {
                updatedKeyFields = options.SourceKeyFields.ToArray();
            }

            // Try Creating Source Object with the updated values.
            if (!TryCreateSourceObject(
                    updatedSourceName,
                    updatedSourceType,
                    updatedSourceParameters,
                    updatedKeyFields,
                    out updatedSourceObject))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// This Method will verify the params required to update relationship info of an entity.
        /// </summary>
        /// <param name="runtimeConfig">runtime config object</param>
        /// <param name="cardinality">cardinality provided by user for update</param>
        /// <param name="targetEntity">name of the target entity for relationship</param>
        /// <returns>Boolean value specifying if given params for update is possible</returns>
        public static bool VerifyCanUpdateRelationship(RuntimeConfig runtimeConfig, string? cardinality, string? targetEntity)
        {
            // CosmosDB doesn't support Relationship
            if (runtimeConfig.DataSource.DatabaseType.Equals(DatabaseType.CosmosDB_NoSQL))
            {
                _logger.LogError("Adding/updating Relationships is currently not supported in CosmosDB.");
                return false;
            }

            // Checking if both cardinality and targetEntity is provided.
            if (cardinality is null || targetEntity is null)
            {
                _logger.LogError("Missing mandatory fields (cardinality and targetEntity) required to configure a relationship.");
                return false;
            }

            // Add/Update of relationship is not allowed when GraphQL is disabled in Global Runtime Settings
            if (!runtimeConfig.IsGraphQLEnabled)
            {
                _logger.LogError("Cannot add/update relationship as GraphQL is disabled in the global runtime settings of the config.");
                return false;
            }

            // Both the source entity and target entity needs to present in config to establish relationship.
            if (!runtimeConfig.Entities.ContainsKey(targetEntity))
            {
                _logger.LogError("Entity: '{targetEntity}' is not present. Relationship cannot be added.", targetEntity);
                return false;
            }

            // Check if provided value of cardinality is present in the enum.
            if (!string.Equals(cardinality, Cardinality.One.ToString(), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(cardinality, Cardinality.Many.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Failed to parse the given cardinality :'{cardinality}'. Supported values are 'one' or 'many'.", cardinality);
                return false;
            }

            // If GraphQL is disabled, entity cannot be used in relationship
            if (!runtimeConfig.Entities[targetEntity].GraphQL.Enabled)
            {
                _logger.LogError("Entity: '{targetEntity}' cannot be used in relationship as it is disabled for GraphQL.", targetEntity);
                return false;
            }

            return true;
        }

        /// <summary>
        /// This Method will create a new Relationship Object based on the given UpdateOptions.
        /// </summary>
        /// <param name="options">update options </param>
        /// <returns>Returns a Relationship Object</returns>
        public static EntityRelationship? CreateNewRelationshipWithUpdateOptions(UpdateOptions options)
        {
            string[]? updatedSourceFields = null;
            string[]? updatedTargetFields = null;
            string[]? updatedLinkingSourceFields = options.LinkingSourceFields is null || !options.LinkingSourceFields.Any() ? null : options.LinkingSourceFields.ToArray();
            string[]? updatedLinkingTargetFields = options.LinkingTargetFields is null || !options.LinkingTargetFields.Any() ? null : options.LinkingTargetFields.ToArray();

            Cardinality updatedCardinality = EnumExtensions.Deserialize<Cardinality>(options.Cardinality!);

            if (options.RelationshipFields is not null && options.RelationshipFields.Any())
            {
                // Getting source and target fields from mapping fields
                //
                if (options.RelationshipFields.Count() != 2)
                {
                    _logger.LogError("Please provide the --relationship.fields in the correct format using ':' between source and target fields.");
                    return null;
                }

                updatedSourceFields = options.RelationshipFields.ElementAt(0).Split(",");
                updatedTargetFields = options.RelationshipFields.ElementAt(1).Split(",");
            }

            return new EntityRelationship(
                Cardinality: updatedCardinality,
                TargetEntity: options.TargetEntity!,
                SourceFields: updatedSourceFields ?? Array.Empty<string>(),
                TargetFields: updatedTargetFields ?? Array.Empty<string>(),
                LinkingObject: options.LinkingObject,
                LinkingSourceFields: updatedLinkingSourceFields ?? Array.Empty<string>(),
                LinkingTargetFields: updatedLinkingTargetFields ?? Array.Empty<string>());
        }

        /// <summary>
        /// This method will try starting the engine.
        /// It will use the config provided by the user, else based on the environment value
        /// it will either merge the config if base config and environmentConfig is present
        /// else it will choose a single config based on precedence (left to right) of
        /// overrides > environmentConfig > defaultConfig
        /// Also preforms validation to check connection string is not null or empty.
        /// </summary>
        public static bool TryStartEngineWithOptions(StartOptions options, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            string? configToBeUsed = options.Config;
            if (string.IsNullOrEmpty(configToBeUsed) && ConfigMerger.TryMergeConfigsIfAvailable(fileSystem, loader, _logger, out configToBeUsed))
            {
                _logger.LogInformation("Using merged config file based on environment: {configToBeUsed}.", configToBeUsed);
            }

            if (!TryGetConfigFileBasedOnCliPrecedence(loader, configToBeUsed, out string runtimeConfigFile))
            {
                _logger.LogError("Config not provided and default config file doesn't exist.");
                return false;
            }

            loader.UpdateConfigFilePath(runtimeConfigFile);

            // Validates that config file has data and follows the correct json schema
            // Replaces all the environment variables while deserializing when starting DAB.
            if (!loader.TryLoadKnownConfig(out RuntimeConfig? deserializedRuntimeConfig, replaceEnvVar: true))
            {
                _logger.LogError("Failed to parse the config file: {runtimeConfigFile}.", runtimeConfigFile);
                return false;
            }
            else
            {
                _logger.LogInformation("Loaded config file: {runtimeConfigFile}", runtimeConfigFile);
            }

            if (string.IsNullOrWhiteSpace(deserializedRuntimeConfig.DataSource.ConnectionString))
            {
                _logger.LogError("Invalid connection-string provided in the config.");
                return false;
            }

            /// This will add arguments to start the runtime engine with the config file.
            List<string> args = new()
            { "--ConfigFileName", runtimeConfigFile };

            /// Add arguments for LogLevel. Checks if LogLevel is overridden with option `--LogLevel`.
            /// If not provided, Default minimum LogLevel is Debug for Development mode and Error for Production mode.
            LogLevel minimumLogLevel;
            if (options.LogLevel is not null)
            {
                if (options.LogLevel is < LogLevel.Trace or > LogLevel.None)
                {
                    _logger.LogError(
                        "LogLevel's valid range is 0 to 6, your value: {logLevel}, see: https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel?view=dotnet-plat-ext-6.0",
                        options.LogLevel);
                    return false;
                }

                minimumLogLevel = (LogLevel)options.LogLevel;
                _logger.LogInformation("Setting minimum LogLevel: {minimumLogLevel}.", minimumLogLevel);
            }
            else
            {
                minimumLogLevel = Startup.GetLogLevelBasedOnMode(deserializedRuntimeConfig);
                HostMode hostModeType = deserializedRuntimeConfig.IsDevelopmentMode() ? HostMode.Development : HostMode.Production;

                _logger.LogInformation("Setting default minimum LogLevel: {minimumLogLevel} for {hostMode} mode.", minimumLogLevel, hostModeType);
            }

            args.Add("--LogLevel");
            args.Add(minimumLogLevel.ToString());

            // This will add args to disable automatic redirects to https if specified by user
            if (options.IsHttpsRedirectionDisabled)
            {
                args.Add(Startup.NO_HTTPS_REDIRECT_FLAG);
            }

            return Azure.DataApiBuilder.Service.Program.StartEngine(args.ToArray());
        }

        /// <summary>
        /// Returns an array of SupportedRestMethods resolved from command line input (EntityOptions).
        /// When no methods are specified, the default "POST" is returned.
        /// </summary>
        /// <param name="options">Entity configuration options received from command line input.</param>
        /// <param name="SupportedRestMethods">Rest methods to enable for stored procedure.</param>
        /// <returns>True when the default (POST) or user provided stored procedure REST methods are supplied.
        /// Returns false and an empty array when an invalid REST method is provided.</returns>
        private static bool TryAddSupportedRestMethodsForStoredProcedure(EntityOptions options, [NotNullWhen(true)] out SupportedHttpVerb[] SupportedRestMethods)
        {
            if (options.RestMethodsForStoredProcedure is null || !options.RestMethodsForStoredProcedure.Any())
            {
                SupportedRestMethods = new[] { SupportedHttpVerb.Post };
            }
            else
            {
                SupportedRestMethods = CreateRestMethods(options.RestMethodsForStoredProcedure);
            }

            return SupportedRestMethods.Length > 0;
        }

        /// <summary>
        /// Identifies the graphQL operations configured for the stored procedure from add command.
        /// When no value is specified, the stored procedure is configured with a mutation operation.
        /// Returns true/false corresponding to a successful/unsuccessful conversion of the operations.
        /// </summary>
        /// <param name="options">GraphQL operations configured for the Stored Procedure using add command</param>
        /// <param name="graphQLOperationForStoredProcedure">GraphQL Operations as Enum type</param>
        /// <returns>True when a user declared GraphQL operation on a stored procedure backed entity is supported. False, otherwise.</returns>
        private static bool TryAddGraphQLOperationForStoredProcedure(EntityOptions options, [NotNullWhen(true)] out GraphQLOperation? graphQLOperationForStoredProcedure)
        {
            if (options.GraphQLOperationForStoredProcedure is null)
            {
                graphQLOperationForStoredProcedure = GraphQLOperation.Mutation;
            }
            else
            {
                if (!TryConvertGraphQLOperationNameToGraphQLOperation(options.GraphQLOperationForStoredProcedure, out GraphQLOperation operation))
                {
                    graphQLOperationForStoredProcedure = null;
                    return false;
                }

                graphQLOperationForStoredProcedure = operation;
            }

            return true;
        }

        /// <summary>
        /// Constructs the updated REST settings based on the input from update command and
        /// existing REST configuration for an entity
        /// </summary>
        /// <param name="entity">Entity for which the REST settings are updated</param>
        /// <param name="options">Input from update command</param>
        /// <returns>Boolean -> when the entity's REST configuration is true/false.
        /// RestEntitySettings -> when a non stored procedure entity is configured with granular REST settings (Path).
        /// RestStoredProcedureEntitySettings -> when a stored procedure entity is configured with explicit SupportedRestMethods.
        /// RestStoredProcedureEntityVerboseSettings-> when a stored procedure entity is configured with explicit SupportedRestMethods and Path settings.</returns>
        private static EntityRestOptions ConstructUpdatedRestDetails(Entity entity, EntityOptions options, bool isCosmosDbNoSql)
        {
            // Updated REST Route details
            EntityRestOptions restPath = (options.RestRoute is not null) ? ConstructRestOptions(restRoute: options.RestRoute, supportedHttpVerbs: null, isCosmosDbNoSql: isCosmosDbNoSql) : entity.Rest;

            // Updated REST Methods info for stored procedures
            SupportedHttpVerb[]? SupportedRestMethods;
            if (!IsStoredProcedureConvertedToOtherTypes(entity, options)
                && (IsStoredProcedure(entity) || IsStoredProcedure(options)))
            {
                if (options.RestMethodsForStoredProcedure is null || !options.RestMethodsForStoredProcedure.Any())
                {
                    SupportedRestMethods = entity.Rest.Methods;
                }
                else
                {
                    SupportedRestMethods = CreateRestMethods(options.RestMethodsForStoredProcedure);
                }
            }
            else
            {
                SupportedRestMethods = null;
            }

            if (!restPath.Enabled)
            {
                // Non-stored procedure scenario when the REST endpoint is disabled for the entity.
                if (options.RestRoute is not null)
                {
                    SupportedRestMethods = null;
                }
                else
                {
                    if (options.RestMethodsForStoredProcedure is not null && options.RestMethodsForStoredProcedure.Any())
                    {
                        restPath = restPath with { Enabled = false };
                    }
                }
            }

            if (IsEntityBeingConvertedToStoredProcedure(entity, options)
               && (SupportedRestMethods is null || SupportedRestMethods.Length == 0))
            {
                SupportedRestMethods = new SupportedHttpVerb[] { SupportedHttpVerb.Post };
            }

            return restPath with { Methods = SupportedRestMethods };
        }

        /// <summary>
        /// Constructs the updated GraphQL settings based on the input from update command and
        /// existing graphQL configuration for an entity
        /// </summary>
        /// <param name="entity">Entity for which GraphQL settings are updated</param>
        /// <param name="options">Input from update command</param>
        /// <returns>Boolean -> when the entity's GraphQL configuration is true/false.
        /// GraphQLEntitySettings -> when a non stored procedure entity is configured with granular GraphQL settings (Type/Singular/Plural).
        /// GraphQLStoredProcedureEntitySettings -> when a stored procedure entity is configured with an explicit operation.
        /// GraphQLStoredProcedureEntityVerboseSettings-> when a stored procedure entity is configured with explicit operation and type settings.</returns>
        private static EntityGraphQLOptions ConstructUpdatedGraphQLDetails(Entity entity, EntityOptions options)
        {
            //Updated GraphQL Type
            EntityGraphQLOptions graphQLType = (options.GraphQLType is not null) ? ConstructGraphQLTypeDetails(options.GraphQLType, null) : entity.GraphQL;
            GraphQLOperation? graphQLOperation;

            if (!IsStoredProcedureConvertedToOtherTypes(entity, options)
                && (IsStoredProcedure(entity) || IsStoredProcedure(options)))
            {
                if (options.GraphQLOperationForStoredProcedure is not null)
                {
                    GraphQLOperation operation;
                    if (TryConvertGraphQLOperationNameToGraphQLOperation(options.GraphQLOperationForStoredProcedure, out operation))
                    {
                        graphQLOperation = operation;
                    }
                    else
                    {
                        graphQLOperation = null;
                    }
                }
                else
                {
                    // When the GraphQL operation for a SP entity has not been specified in the update command,
                    // assign the existing GraphQL operation.
                    graphQLOperation = entity.GraphQL.Operation;
                }
            }
            else
            {
                graphQLOperation = null;
            }

            if (!graphQLType.Enabled)
            {
                if (options.GraphQLType is not null)
                {
                    graphQLOperation = null;
                }
                else
                {
                    if (options.GraphQLOperationForStoredProcedure is null)
                    {
                        graphQLOperation = null;
                    }
                    else
                    {
                        graphQLType = graphQLType with { Enabled = false };
                    }
                }
            }

            if (IsEntityBeingConvertedToStoredProcedure(entity, options) && graphQLOperation is null)
            {
                graphQLOperation = GraphQLOperation.Mutation;
            }

            return graphQLType with { Operation = graphQLOperation };
        }
    }
}
