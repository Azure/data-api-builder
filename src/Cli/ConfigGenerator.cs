// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.NamingPolicies;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core;
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
            Dictionary<string, object?> dbOptions = new();

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

            bool isMultipleCreateEnabledForGraphQL;

            // Multiple mutation operations are applicable only for MSSQL database. When the option --graphql.multiple-create.enabled is specified for other database types,
            // a warning is logged.
            // When multiple mutation operations are extended for other database types, this option should be honored.
            // Tracked by issue #2001: https://github.com/Azure/data-api-builder/issues/2001.
            if (dbType is not DatabaseType.MSSQL && options.MultipleCreateOperationEnabled is not CliBool.None)
            {
                _logger.LogWarning($"The option --graphql.multiple-create.enabled is not supported for the {dbType.ToString()} database type and will not be honored.");
            }

            MultipleMutationOptions? multipleMutationOptions = null;

            // Multiple mutation operations are applicable only for MSSQL database. When the option --graphql.multiple-create.enabled is specified for other database types,
            // it is not honored.
            if (dbType is DatabaseType.MSSQL && options.MultipleCreateOperationEnabled is not CliBool.None)
            {
                isMultipleCreateEnabledForGraphQL = IsMultipleCreateOperationEnabled(options.MultipleCreateOperationEnabled);
                multipleMutationOptions = new(multipleCreateOptions: new MultipleCreateOptions(enabled: isMultipleCreateEnabledForGraphQL));
            }

            switch (dbType)
            {
                case DatabaseType.CosmosDB_NoSQL:
                    // If cosmosdb_nosql is specified, rest is disabled.
                    restEnabled = false;

                    string? cosmosDatabase = options.CosmosNoSqlDatabase;
                    string? cosmosContainer = options.CosmosNoSqlContainer;
                    string? graphQLSchemaPath = options.GraphQLSchemaPath;
                    if (string.IsNullOrEmpty(cosmosDatabase))
                    {
                        _logger.LogError("Missing mandatory configuration options for CosmosDB_NoSql: --cosmosdb_nosql-database, and --graphql-schema");
                        return false;
                    }

                    if (string.IsNullOrEmpty(graphQLSchemaPath))
                    {
                        graphQLSchemaPath = "schema.gql"; // Default to schema.gql

                        _logger.LogWarning("The GraphQL schema path, i.e. --graphql-schema, is not specified. Please either provide your schema or generate the schema using the `export` command before running `dab start`. For more detail, run 'dab export --help` ");
                    }
                    else if (!fileSystem.File.Exists(graphQLSchemaPath))
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

                    if (options.RestRequestBodyStrict is not CliBool.None)
                    {
                        _logger.LogWarning("Configuration option --rest.request-body-strict is not honored for cosmosdb_nosql since CosmosDB does not support REST.");
                    }

                    restPath = null;
                    dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database)), cosmosDatabase);
                    dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Container)), cosmosContainer);
                    dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Schema)), graphQLSchemaPath);
                    break;

                case DatabaseType.DWSQL:
                case DatabaseType.MSSQL:
                    dbOptions.Add(namingPolicy.ConvertName(nameof(MsSqlOptions.SetSessionContext)), options.SetSessionContext);

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
                _logger.LogError("{apiType} path {message}", ApiType.REST, RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG);
                return false;
            }

            if (!IsURIComponentValid(options.GraphQLPath))
            {
                _logger.LogError("{apiType} path {message}", ApiType.GraphQL, RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG);
                return false;
            }

            if (!IsURIComponentValid(runtimeBaseRoute))
            {
                _logger.LogError("Runtime base-route {message}", RuntimeConfigValidatorUtil.URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG);
                return false;
            }

            if (runtimeBaseRoute is not null)
            {
                if (!Enum.TryParse(options.AuthenticationProvider, ignoreCase: true, out EasyAuthType authMode) || authMode is not EasyAuthType.StaticWebApps)
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
                    Rest: new(restEnabled, restPath ?? RestRuntimeOptions.DEFAULT_PATH, options.RestRequestBodyStrict is CliBool.False ? false : true),
                    GraphQL: new(Enabled: graphQLEnabled, Path: graphQLPath, MultipleMutationOptions: multipleMutationOptions),
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
        /// Helper method to determine if the multiple create operation is enabled or not based on the inputs from dab init command.
        /// </summary>
        /// <param name="multipleCreateEnabledOptionValue">Input value for --graphql.multiple-create.enabled option of the init command</param>
        /// <returns>True/False</returns>
        private static bool IsMultipleCreateOperationEnabled(CliBool multipleCreateEnabledOptionValue)
        {
            return multipleCreateEnabledOptionValue is CliBool.True;
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
            EntityCacheOptions? cacheOptions = ConstructCacheOptions(options.CacheEnabled, options.CacheTtl);

            // Create new entity.
            Entity entity = new(
                Source: source,
                Rest: restOptions,
                GraphQL: graphqlOptions,
                Permissions: permissionSettings,
                Relationships: null,
                Mappings: null,
                Cache: cacheOptions);

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
        /// Tries to update the runtime settings based on the provided runtime options.
        /// </summary>
        /// <returns>True if the update was successful, false otherwise.</returns>
        public static bool TryConfigureSettings(ConfigureOptions options, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
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

            if (!TryUpdateConfiguredDataSourceOptions(options, ref runtimeConfig))
            {
                return false;
            }

            if (!TryUpdateConfiguredRuntimeOptions(options, ref runtimeConfig))
            {
                return false;
            }

            if (options.DepthLimit is not null && !TryUpdateDepthLimit(options, ref runtimeConfig))
            {
                return false;
            }

            if (!TryUpdateConfiguredAzureKeyVaultOptions(options, ref runtimeConfig))
            {
                return false;
            }

            return WriteRuntimeConfigToFile(runtimeConfigFile, runtimeConfig, fileSystem);
        }

        /// <summary>
        /// Configures the data source options for the runtimeconfig based on the provided options.
        /// This method updates the database type, connection string, and other database-specific options in the config file.
        /// It validates the provided database type and ensures that options specific to certain database types are correctly applied.
        /// When validation fails, this function logs the validation errors and returns false.
        /// </summary>
        /// <param name="options">The configuration options provided by the user.</param>
        /// <param name="runtimeConfig">The runtime configuration to be updated. This parameter is passed by reference and must not be null if the method returns true.</param>
        /// <returns>
        /// True if the data source options were successfully configured and the runtime configuration was updated; otherwise, false.
        /// </returns>
        private static bool TryUpdateConfiguredDataSourceOptions(
            ConfigureOptions options,
            [NotNullWhen(true)] ref RuntimeConfig runtimeConfig)
        {
            DatabaseType dbType = runtimeConfig.DataSource.DatabaseType;
            string dataSourceConnectionString = runtimeConfig.DataSource.ConnectionString;
            DatasourceHealthCheckConfig? datasourceHealthCheckConfig = runtimeConfig.DataSource.Health;

            if (options.DataSourceDatabaseType is not null)
            {
                if (!Enum.TryParse(options.DataSourceDatabaseType, ignoreCase: true, out dbType))
                {
                    _logger.LogError(EnumExtensions.GenerateMessageForInvalidInput<DatabaseType>(options.DataSourceDatabaseType));
                    return false;
                }
            }

            if (options.DataSourceConnectionString is not null)
            {
                dataSourceConnectionString = options.DataSourceConnectionString;
            }

            Dictionary<string, object?>? dbOptions = new();
            HyphenatedNamingPolicy namingPolicy = new();

            if (DatabaseType.CosmosDB_NoSQL.Equals(dbType))
            {
                AddCosmosDbOptions(dbOptions, options, namingPolicy);
            }
            else if (!string.IsNullOrWhiteSpace(options.DataSourceOptionsDatabase)
                    || !string.IsNullOrWhiteSpace(options.DataSourceOptionsContainer)
                    || !string.IsNullOrWhiteSpace(options.DataSourceOptionsSchema))
            {
                _logger.LogError("Database, Container, and Schema options are only applicable for CosmosDB_NoSQL database type.");
                return false;
            }

            if (options.DataSourceOptionsSetSessionContext is not null)
            {
                if (!(DatabaseType.MSSQL.Equals(dbType) || DatabaseType.DWSQL.Equals(dbType)))
                {
                    _logger.LogError("SetSessionContext option is only applicable for MSSQL/DWSQL database type.");
                    return false;
                }

                dbOptions.Add(namingPolicy.ConvertName(nameof(MsSqlOptions.SetSessionContext)), options.DataSourceOptionsSetSessionContext.Value);
            }

            dbOptions = EnumerableUtilities.IsNullOrEmpty(dbOptions) ? null : dbOptions;
            DataSource dataSource = new(dbType, dataSourceConnectionString, dbOptions, datasourceHealthCheckConfig);
            runtimeConfig = runtimeConfig with { DataSource = dataSource };

            return runtimeConfig != null;
        }

        /// <summary>
        /// Adds CosmosDB-specific options to the provided database options dictionary.
        /// This method checks if the CosmosDB-specific options (database, container, and schema) are provided in the
        /// configuration options. If they are, it converts their names using the provided naming policy and adds them
        /// to the database options dictionary.
        /// </summary>
        /// <param name="dbOptions">The dictionary to which the CosmosDB-specific options will be added.</param>
        /// <param name="options">The configuration options provided by the user.</param>
        /// <param name="namingPolicy">The naming policy used to convert option names to the desired format.</param>
        private static void AddCosmosDbOptions(Dictionary<string, object?> dbOptions, ConfigureOptions options, HyphenatedNamingPolicy namingPolicy)
        {
            if (!string.IsNullOrWhiteSpace(options.DataSourceOptionsDatabase))
            {
                dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Database)), options.DataSourceOptionsDatabase);
            }

            if (!string.IsNullOrWhiteSpace(options.DataSourceOptionsContainer))
            {
                dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Container)), options.DataSourceOptionsContainer);
            }

            if (!string.IsNullOrWhiteSpace(options.DataSourceOptionsSchema))
            {
                dbOptions.Add(namingPolicy.ConvertName(nameof(CosmosDbNoSQLDataSourceOptions.Schema)), options.DataSourceOptionsSchema);
            }
        }

        /// <summary>
        /// Attempts to update the depth limit in the GraphQL runtime settings based on the provided value.
        /// Validates that any user-provided depth limit is an integer within the valid range of [1 to Int32.MaxValue] or -1.
        /// A depth limit of -1 is considered a special case that disables the GraphQL depth limit.
        /// [NOTE:] This method expects the provided depth limit to be not null.
        /// </summary>
        /// <param name="options">Options including the new depth limit.</param>
        /// <param name="runtimeConfig">Current config, updated if method succeeds.</param>
        /// <returns>True if the update was successful, false otherwise.</returns>
        private static bool TryUpdateDepthLimit(
            ConfigureOptions options,
            [NotNullWhen(true)] ref RuntimeConfig runtimeConfig)
        {
            // check if depth limit is within the valid range of 1 to Int32.MaxValue
            int? newDepthLimit = options.DepthLimit;
            if (newDepthLimit < 1)
            {
                if (newDepthLimit == -1)
                {
                    _logger.LogWarning("Depth limit set to -1 removes the GraphQL query depth limit.");
                }
                else
                {
                    _logger.LogError("Invalid depth limit. Specify a depth limit > 0 or remove the existing depth limit by specifying -1.");
                    return false;
                }
            }

            // Try to update the depth limit in the runtime configuration
            try
            {
                runtimeConfig = runtimeConfig with { Runtime = runtimeConfig.Runtime! with { GraphQL = runtimeConfig.Runtime.GraphQL! with { DepthLimit = newDepthLimit, UserProvidedDepthLimit = true } } };
            }
            catch (Exception e)
            {
                _logger.LogError("Failed to update the depth limit: {e}", e);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to update the Config parameters in the runtime settings based on the provided value.
        /// Performs the update on the runtimeConfig which is passed as reference
        /// Returns true if the update has been performed, else false
        /// Currently, used to update only GraphQL settings
        /// </summary>
        /// <param name="options">Options including the graphql runtime parameters.</param>
        /// <param name="runtimeConfig">Current config, updated if method succeeds.</param>
        /// <returns>True if the update was successful, false otherwise.</returns>
        private static bool TryUpdateConfiguredRuntimeOptions(
            ConfigureOptions options,
            [NotNullWhen(true)] ref RuntimeConfig runtimeConfig)
        {
            // Rest: Enabled, Path, and Request.Body.Strict
            if (options.RuntimeRestEnabled != null ||
                options.RuntimeRestPath != null ||
                options.RuntimeRestRequestBodyStrict != null)
            {
                RestRuntimeOptions? updatedRestOptions = runtimeConfig?.Runtime?.Rest ?? new();
                bool status = TryUpdateConfiguredRestValues(options, ref updatedRestOptions);
                if (status)
                {
                    runtimeConfig = runtimeConfig! with { Runtime = runtimeConfig.Runtime! with { Rest = updatedRestOptions } };
                }
                else
                {
                    return false;
                }
            }

            // GraphQL: Enabled, Path, Allow-Introspection and Multiple-Mutations.Create.Enabled
            if (options.RuntimeGraphQLEnabled != null ||
                options.RuntimeGraphQLPath != null ||
                options.RuntimeGraphQLAllowIntrospection != null ||
                options.RuntimeGraphQLMultipleMutationsCreateEnabled != null)
            {
                GraphQLRuntimeOptions? updatedGraphQLOptions = runtimeConfig?.Runtime?.GraphQL ?? new();
                bool status = TryUpdateConfiguredGraphQLValues(options, ref updatedGraphQLOptions);
                if (status)
                {
                    runtimeConfig = runtimeConfig! with { Runtime = runtimeConfig.Runtime! with { GraphQL = updatedGraphQLOptions } };
                }
                else
                {
                    return false;
                }
            }

            // Cache: Enabled and TTL
            if (options.RuntimeCacheEnabled != null ||
                options.RuntimeCacheTTL != null)
            {
                RuntimeCacheOptions? updatedCacheOptions = runtimeConfig?.Runtime?.Cache ?? new();
                bool status = TryUpdateConfiguredCacheValues(options, ref updatedCacheOptions);
                if (status)
                {
                    runtimeConfig = runtimeConfig! with { Runtime = runtimeConfig.Runtime! with { Cache = updatedCacheOptions } };
                }
                else
                {
                    return false;
                }
            }

            // Host: Mode, Cors.Origins, Cors.AllowCredentials, Authentication.Provider, Authentication.Jwt.Audience, Authentication.Jwt.Issuer
            if (options.RuntimeHostMode != null ||
                options.RuntimeHostCorsOrigins != null ||
                options.RuntimeHostCorsAllowCredentials != null ||
                options.RuntimeHostAuthenticationProvider != null ||
                options.RuntimeHostAuthenticationJwtAudience != null ||
                options.RuntimeHostAuthenticationJwtIssuer != null)
            {
                HostOptions? updatedHostOptions = runtimeConfig?.Runtime?.Host;
                bool status = TryUpdateConfiguredHostValues(options, ref updatedHostOptions);
                if (status)
                {
                    runtimeConfig = runtimeConfig! with { Runtime = runtimeConfig.Runtime! with { Host = updatedHostOptions } };
                }
                else
                {
                    return false;
                }
            }

            // Telemetry: Azure Log Analytics
            if (options.AzureLogAnalyticsEnabled is not null ||
                options.AzureLogAnalyticsDabIdentifier is not null ||
                options.AzureLogAnalyticsFlushIntervalSeconds is not null ||
                options.AzureLogAnalyticsCustomTableName is not null ||
                options.AzureLogAnalyticsDcrImmutableId is not null ||
                options.AzureLogAnalyticsDceEndpoint is not null)
            {
                AzureLogAnalyticsOptions updatedAzureLogAnalyticsOptions = runtimeConfig?.Runtime?.Telemetry?.AzureLogAnalytics ?? new();
                bool status = TryUpdateConfiguredAzureLogAnalyticsOptions(options, ref updatedAzureLogAnalyticsOptions);
                if (status)
                {
                    runtimeConfig = runtimeConfig! with { Runtime = runtimeConfig.Runtime! with { Telemetry = runtimeConfig.Runtime!.Telemetry is not null ? runtimeConfig.Runtime!.Telemetry with { AzureLogAnalytics = updatedAzureLogAnalyticsOptions } : new TelemetryOptions(AzureLogAnalytics: updatedAzureLogAnalyticsOptions) } };
                }
                else
                {
                    return false;
                }
            }

            return runtimeConfig != null;
        }

        /// <summary>
        /// Attempts to update the Config parameters in the Rest runtime settings based on the provided value.
        /// Validates that any user-provided values are valid and then returns true if the updated Rest options
        /// need to be overwritten on the existing config parameters
        /// </summary>
        /// <param name="options">options.</param>
        /// <param name="updatedRestOptions">updatedRestOptions.</param>
        /// <returns>True if the value needs to be updated in the runtime config, else false</returns>
        private static bool TryUpdateConfiguredRestValues(ConfigureOptions options, ref RestRuntimeOptions? updatedRestOptions)
        {
            object? updatedValue;
            try
            {
                // Runtime.Rest.Enabled
                updatedValue = options?.RuntimeRestEnabled;
                if (updatedValue != null)
                {
                    updatedRestOptions = updatedRestOptions! with { Enabled = (bool)updatedValue };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.Rest.Enabled as '{updatedValue}'", updatedValue);
                }

                // Runtime.Rest.Path
                updatedValue = options?.RuntimeRestPath;
                if (updatedValue != null)
                {
                    bool status = RuntimeConfigValidatorUtil.TryValidateUriComponent(uriComponent: (string)updatedValue, out string exceptionMessage);
                    if (status)
                    {
                        updatedRestOptions = updatedRestOptions! with { Path = (string)updatedValue };
                        _logger.LogInformation("Updated RuntimeConfig with Runtime.Rest.Path as '{updatedValue}'", updatedValue);
                    }
                    else
                    {
                        _logger.LogError("Failed to update RuntimeConfig with Runtime.Rest.Path " +
                            $"as '{updatedValue}'. Error details: {exceptionMessage}", exceptionMessage);
                        return false;
                    }
                }

                // Runtime.Rest.Request-Body-Strict
                updatedValue = options?.RuntimeRestRequestBodyStrict;
                if (updatedValue != null)
                {
                    updatedRestOptions = updatedRestOptions! with { RequestBodyStrict = (bool)updatedValue };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.Rest.Request-Body-Strict as '{updatedValue}'", updatedValue);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to update RuntimeConfig.Rest with exception message: {exceptionMessage}.", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Attempts to update the Config parameters in the GraphQL runtime settings based on the provided value.
        /// Validates that any user-provided parameter value is valid and then returns true if the updated GraphQL options
        /// needs to be overwritten on the existing config parameters
        /// </summary>
        /// <param name="options">options.</param>
        /// <param name="updatedGraphQLOptions">updatedGraphQLOptions.</param>
        /// <returns>True if the value needs to be updated in the runtime config, else false</returns>
        private static bool TryUpdateConfiguredGraphQLValues(
            ConfigureOptions options,
            ref GraphQLRuntimeOptions? updatedGraphQLOptions)
        {
            object? updatedValue;
            try
            {
                // Runtime.GraphQL.Enabled
                updatedValue = options?.RuntimeGraphQLEnabled;
                if (updatedValue != null)
                {
                    updatedGraphQLOptions = updatedGraphQLOptions! with { Enabled = (bool)updatedValue };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.GraphQL.Enabled as '{updatedValue}'", updatedValue);
                }

                // Runtime.GraphQL.Path
                updatedValue = options?.RuntimeGraphQLPath;
                if (updatedValue != null)
                {
                    bool status = RuntimeConfigValidatorUtil.TryValidateUriComponent(uriComponent: (string)updatedValue, out string exceptionMessage);
                    if (status)
                    {
                        updatedGraphQLOptions = updatedGraphQLOptions! with { Path = (string)updatedValue };
                        _logger.LogInformation("Updated RuntimeConfig with Runtime.GraphQL.Path as '{updatedValue}'", updatedValue);
                    }
                    else
                    {
                        _logger.LogError("Failed to update Runtime.GraphQL.Path as '{updatedValue}' due to exception message: {exceptionMessage}", updatedValue, exceptionMessage);
                        return false;
                    }
                }

                // Runtime.GraphQL.Allow-Introspection
                updatedValue = options?.RuntimeGraphQLAllowIntrospection;
                if (updatedValue != null)
                {
                    updatedGraphQLOptions = updatedGraphQLOptions! with { AllowIntrospection = (bool)updatedValue };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.GraphQL.AllowIntrospection as '{updatedValue}'", updatedValue);
                }

                // Runtime.GraphQL.Multiple-mutations.Create.Enabled
                updatedValue = options?.RuntimeGraphQLMultipleMutationsCreateEnabled;
                if (updatedValue != null)
                {
                    MultipleCreateOptions multipleCreateOptions = new(enabled: (bool)updatedValue);
                    updatedGraphQLOptions = updatedGraphQLOptions! with { MultipleMutationOptions = new(multipleCreateOptions) };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.GraphQL.Multiple-Mutations.Create.Enabled as '{updatedValue}'", updatedValue);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to update RuntimeConfig.GraphQL with exception message: {exceptionMessage}.", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Attempts to update the Config parameters in the Cache runtime settings based on the provided value.
        /// Validates user-provided parameters and then returns true if the updated Cache options
        /// need to be overwritten on the existing config parameters
        /// </summary>
        /// <param name="options">options.</param>
        /// <param name="updatedCacheOptions">updatedCacheOptions.</param>
        /// <returns>True if the value needs to be updated in the runtime config, else false</returns>
        private static bool TryUpdateConfiguredCacheValues(
            ConfigureOptions options,
            ref RuntimeCacheOptions? updatedCacheOptions)
        {
            object? updatedValue;
            try
            {
                // Runtime.Cache.Enabled
                updatedValue = options?.RuntimeCacheEnabled;
                if (updatedValue != null)
                {
                    updatedCacheOptions = updatedCacheOptions! with { Enabled = (bool)updatedValue };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.Cache.Enabled as '{updatedValue}'", updatedValue);
                }

                // Runtime.Cache.ttl-seconds
                updatedValue = options?.RuntimeCacheTTL;
                if (updatedValue != null)
                {
                    bool status = RuntimeConfigValidatorUtil.IsTTLValid(ttl: (int)updatedValue);
                    if (status)
                    {
                        updatedCacheOptions = updatedCacheOptions! with { TtlSeconds = (int)updatedValue, UserProvidedTtlOptions = true };
                        _logger.LogInformation("Updated RuntimeConfig with Runtime.Cache.ttl-seconds as '{updatedValue}'", updatedValue);
                    }
                    else
                    {
                        _logger.LogError("Failed to update Runtime.Cache.ttl-seconds as '{updatedValue}' value in TTL is not valid.", updatedValue);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to update RuntimeConfig.Cache with exception message: {exceptionMessage}.", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Attempts to update the Config parameters in the Host runtime settings based on the provided value.
        /// Validates that any user-provided parameter value is valid and then returns true if the updated Host options
        /// needs to be overwritten on the existing config parameters
        /// </summary>
        /// <param name="options">options.</param>
        /// <param name="updatedHostOptions">updatedHostOptions.</param>
        /// <returns>True if the value needs to be updated in the runtime config, else false</returns>
        private static bool TryUpdateConfiguredHostValues(
            ConfigureOptions options,
            ref HostOptions? updatedHostOptions)
        {
            object? updatedValue;
            try
            {
                // Runtime.Host.Mode
                updatedValue = options?.RuntimeHostMode;
                if (updatedValue != null)
                {
                    updatedHostOptions = updatedHostOptions! with { Mode = (HostMode)updatedValue };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.Host.Mode as '{updatedValue}'", updatedValue);
                }

                // Runtime.Host.Cors.Origins
                IEnumerable<string>? updatedCorsOrigins = options?.RuntimeHostCorsOrigins;
                if (updatedCorsOrigins != null && updatedCorsOrigins.Any())
                {
                    CorsOptions corsOptions;
                    if (updatedHostOptions?.Cors == null)
                    {
                        corsOptions = new(Origins: updatedCorsOrigins.ToArray());
                    }
                    else
                    {
                        corsOptions = updatedHostOptions.Cors! with { Origins = updatedCorsOrigins.ToArray() };
                    }

                    updatedHostOptions = updatedHostOptions! with { Cors = corsOptions };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.Host.Cors.Origins as '{updatedValue}'", updatedCorsOrigins);
                }

                // Runtime.Host.Cors.Allow-Credentials
                updatedValue = options?.RuntimeHostCorsAllowCredentials;
                if (updatedValue != null)
                {
                    CorsOptions corsOptions;
                    if (updatedHostOptions?.Cors == null)
                    {
                        corsOptions = new(new string[] { }, AllowCredentials: (bool)updatedValue);
                    }
                    else
                    {
                        corsOptions = updatedHostOptions.Cors! with { AllowCredentials = (bool)updatedValue };
                    }

                    updatedHostOptions = updatedHostOptions! with { Cors = corsOptions };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.Host.Cors.Allow-Credentials as '{updatedValue}'", updatedValue);
                }

                // Runtime.Host.Authentication.Provider
                string? updatedProviderValue = options?.RuntimeHostAuthenticationProvider;
                if (updatedProviderValue != null)
                {
                    updatedValue = updatedProviderValue?.ToString() ?? nameof(EasyAuthType.StaticWebApps);
                    AuthenticationOptions AuthOptions;
                    if (updatedHostOptions?.Authentication == null)
                    {
                        AuthOptions = new(Provider: (string)updatedValue);
                    }
                    else
                    {
                        AuthOptions = updatedHostOptions.Authentication with { Provider = (string)updatedValue };
                    }

                    updatedHostOptions = updatedHostOptions! with { Authentication = AuthOptions };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.Host.Authentication.Provider as '{updatedValue}'", updatedValue);
                }

                // Runtime.Host.Authentication.Jwt.Audience
                updatedValue = options?.RuntimeHostAuthenticationJwtAudience;
                if (updatedValue != null)
                {
                    JwtOptions jwtOptions;
                    AuthenticationOptions AuthOptions;
                    if (updatedHostOptions?.Authentication == null || updatedHostOptions.Authentication?.Jwt == null)
                    {
                        jwtOptions = new(Audience: (string)updatedValue, null);
                    }
                    else
                    {
                        jwtOptions = updatedHostOptions.Authentication.Jwt with { Audience = (string)updatedValue };
                    }

                    if (updatedHostOptions?.Authentication == null)
                    {
                        AuthOptions = new(Jwt: jwtOptions);
                    }
                    else
                    {
                        AuthOptions = updatedHostOptions.Authentication with { Jwt = jwtOptions };
                    }

                    updatedHostOptions = updatedHostOptions! with { Authentication = AuthOptions };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.Host.Authentication.Jwt.Audience as '{updatedValue}'", updatedValue);
                }

                // Runtime.Host.Authentication.Jwt.Issuer
                updatedValue = options?.RuntimeHostAuthenticationJwtIssuer;
                if (updatedValue != null)
                {
                    JwtOptions jwtOptions;
                    AuthenticationOptions AuthOptions;
                    if (updatedHostOptions?.Authentication == null || updatedHostOptions.Authentication?.Jwt == null)
                    {
                        jwtOptions = new(null, Issuer: (string)updatedValue);
                    }
                    else
                    {
                        jwtOptions = updatedHostOptions.Authentication.Jwt with { Issuer = (string)updatedValue };
                    }

                    if (updatedHostOptions?.Authentication == null)
                    {
                        AuthOptions = new(Jwt: jwtOptions);
                    }
                    else
                    {
                        AuthOptions = updatedHostOptions.Authentication with { Jwt = jwtOptions };
                    }

                    updatedHostOptions = updatedHostOptions! with { Authentication = AuthOptions };
                    _logger.LogInformation("Updated RuntimeConfig with Runtime.Host.Authentication.Jwt.Issuer as '{updatedValue}'", updatedValue);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to update RuntimeConfig.Host with exception message: {exceptionMessage}.", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Attempts to update the Azure Log Analytics configuration options based on the provided values.
        /// Validates that any user-provided parameter value is valid and updates the runtime configuration accordingly.
        /// </summary>
        /// <param name="options">The configuration options provided by the user.</param>
        /// <param name="azureLogAnalyticsOptions">The Azure Log Analytics options to be updated.</param>
        /// <returns>True if the Azure Log Analytics options were successfully configured; otherwise, false.</returns>
        private static bool TryUpdateConfiguredAzureLogAnalyticsOptions(
            ConfigureOptions options,
            ref AzureLogAnalyticsOptions azureLogAnalyticsOptions)
        {
            try
            {
                AzureLogAnalyticsAuthOptions? updatedAuthOptions = azureLogAnalyticsOptions.Auth;

                // Runtime.Telemetry.AzureLogAnalytics.Enabled
                if (options.AzureLogAnalyticsEnabled is not null)
                {
                    azureLogAnalyticsOptions = azureLogAnalyticsOptions with { Enabled = options.AzureLogAnalyticsEnabled is CliBool.True, UserProvidedEnabled = true };
                    _logger.LogInformation($"Updated configuration with runtime.telemetry.azure-log-analytics.enabled as '{options.AzureLogAnalyticsEnabled}'");
                }

                // Runtime.Telemetry.AzureLogAnalytics.DabIdentifier
                if (options.AzureLogAnalyticsDabIdentifier is not null)
                {
                    azureLogAnalyticsOptions = azureLogAnalyticsOptions with { DabIdentifier = options.AzureLogAnalyticsDabIdentifier, UserProvidedDabIdentifier = true };
                    _logger.LogInformation($"Updated configuration with runtime.telemetry.azure-log-analytics.dab-identifier as '{options.AzureLogAnalyticsDabIdentifier}'");
                }

                // Runtime.Telemetry.AzureLogAnalytics.FlushIntervalSeconds
                if (options.AzureLogAnalyticsFlushIntervalSeconds is not null)
                {
                    if (options.AzureLogAnalyticsFlushIntervalSeconds <= 0)
                    {
                        _logger.LogError("Failed to update configuration with runtime.telemetry.azure-log-analytics.flush-interval-seconds. Value must be a positive integer greater than 0.");
                        return false;
                    }

                    azureLogAnalyticsOptions = azureLogAnalyticsOptions with { FlushIntervalSeconds = options.AzureLogAnalyticsFlushIntervalSeconds, UserProvidedFlushIntervalSeconds = true };
                    _logger.LogInformation($"Updated configuration with runtime.telemetry.azure-log-analytics.flush-interval-seconds as '{options.AzureLogAnalyticsFlushIntervalSeconds}'");
                }

                // Runtime.Telemetry.AzureLogAnalytics.Auth.CustomTableName
                if (options.AzureLogAnalyticsCustomTableName is not null)
                {
                    updatedAuthOptions = updatedAuthOptions is not null
                        ? updatedAuthOptions with { CustomTableName = options.AzureLogAnalyticsCustomTableName, UserProvidedCustomTableName = true }
                        : new AzureLogAnalyticsAuthOptions { CustomTableName = options.AzureLogAnalyticsCustomTableName, UserProvidedCustomTableName = true };
                    _logger.LogInformation($"Updated configuration with runtime.telemetry.azure-log-analytics.auth.custom-table-name as '{options.AzureLogAnalyticsCustomTableName}'");
                }

                // Runtime.Telemetry.AzureLogAnalytics.Auth.DcrImmutableId
                if (options.AzureLogAnalyticsDcrImmutableId is not null)
                {
                    updatedAuthOptions = updatedAuthOptions is not null
                        ? updatedAuthOptions with { DcrImmutableId = options.AzureLogAnalyticsDcrImmutableId, UserProvidedDcrImmutableId = true }
                        : new AzureLogAnalyticsAuthOptions { DcrImmutableId = options.AzureLogAnalyticsDcrImmutableId, UserProvidedDcrImmutableId = true };
                    _logger.LogInformation($"Updated configuration with runtime.telemetry.azure-log-analytics.auth.dcr-immutable-id as '{options.AzureLogAnalyticsDcrImmutableId}'");
                }

                // Runtime.Telemetry.AzureLogAnalytics.Auth.DceEndpoint
                if (options.AzureLogAnalyticsDceEndpoint is not null)
                {
                    updatedAuthOptions = updatedAuthOptions is not null
                        ? updatedAuthOptions with { DceEndpoint = options.AzureLogAnalyticsDceEndpoint, UserProvidedDceEndpoint = true }
                        : new AzureLogAnalyticsAuthOptions { DceEndpoint = options.AzureLogAnalyticsDceEndpoint, UserProvidedDceEndpoint = true };
                    _logger.LogInformation($"Updated configuration with runtime.telemetry.azure-log-analytics.auth.dce-endpoint as '{options.AzureLogAnalyticsDceEndpoint}'");
                }

                // Update Azure Log Analytics options with Auth options if it was modified
                if (updatedAuthOptions is not null)
                {
                    azureLogAnalyticsOptions = azureLogAnalyticsOptions with { Auth = updatedAuthOptions };
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to update configuration with runtime.telemetry.azure-log-analytics. Exception message: {ex.Message}.");
                return false;
            }
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
            EntityCacheOptions? updatedCacheOptions = ConstructCacheOptions(options.CacheEnabled, options.CacheTtl);

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
                Mappings: updatedMappings,
                Cache: updatedCacheOptions);
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
            if (!TryGetConfigForRuntimeEngine(options.Config, loader, fileSystem, out string runtimeConfigFile))
            {
                return false;
            }

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
                        "LogLevel's valid range is 0 to 6, your value: {logLevel}, see: https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.loglevel",
                        options.LogLevel);
                    return false;
                }

                minimumLogLevel = (LogLevel)options.LogLevel;
                _logger.LogInformation("Setting minimum LogLevel: {minimumLogLevel}.", minimumLogLevel);
            }
            else
            {
                minimumLogLevel = deserializedRuntimeConfig.GetConfiguredLogLevel();
                HostMode hostModeType = deserializedRuntimeConfig.IsDevelopmentMode() ? HostMode.Development : HostMode.Production;

                _logger.LogInformation($"Setting default minimum LogLevel: {minimumLogLevel} for {hostModeType} mode.", minimumLogLevel, hostModeType);
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
        /// Runs all the validations on the config file and returns true if the config is valid.
        /// </summary>
        public static bool IsConfigValid(ValidateOptions options, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
        {
            if (!TryGetConfigForRuntimeEngine(options.Config, loader, fileSystem, out string runtimeConfigFile))
            {
                return false;
            }

            _logger.LogInformation("Validating config file: {runtimeConfigFile}", runtimeConfigFile);

            RuntimeConfigProvider runtimeConfigProvider = new(loader);

            ILogger<RuntimeConfigValidator> runtimeConfigValidatorLogger = LoggerFactoryForCli.CreateLogger<RuntimeConfigValidator>();
            RuntimeConfigValidator runtimeConfigValidator = new(runtimeConfigProvider, fileSystem, runtimeConfigValidatorLogger, true);

            return runtimeConfigValidator.TryValidateConfig(runtimeConfigFile, LoggerFactoryForCli).Result;
        }

        /// <summary>
        /// Tries to fetch the config file based on the precedence.
        /// If config provided by the user, it will be the final config used, else will check based on the environment variable.
        /// Returns true if the config file is found, else false.
        /// </summary>
        public static bool TryGetConfigForRuntimeEngine(
            string? configToBeUsed,
            FileSystemRuntimeConfigLoader loader,
            IFileSystem fileSystem,
            out string runtimeConfigFile)
        {
            if (string.IsNullOrEmpty(configToBeUsed) && ConfigMerger.TryMergeConfigsIfAvailable(fileSystem, loader, _logger, out configToBeUsed))
            {
                _logger.LogInformation("Using merged config file based on environment: {configToBeUsed}.", configToBeUsed);
            }

            if (!TryGetConfigFileBasedOnCliPrecedence(loader, configToBeUsed, out runtimeConfigFile))
            {
                _logger.LogError("Config not provided and default config file doesn't exist.");
                return false;
            }

            loader.UpdateConfigFilePath(runtimeConfigFile);

            return true;
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

        /// <summary>
        /// This method will add the telemetry options to the config file. If the config file already has telemetry options,
        /// it will overwrite the existing options.
        /// Data API builder consumes the config file with provided telemetry options to send telemetry to Application Insights.
        /// </summary>
        public static bool TryAddTelemetry(AddTelemetryOptions options, FileSystemRuntimeConfigLoader loader, IFileSystem fileSystem)
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

            if (runtimeConfig.Runtime is null)
            {
                _logger.LogError("Invalid or missing 'runtime' section in config file: {runtimeConfigFile}.", runtimeConfigFile);
                return false;
            }

            if (options.AppInsightsEnabled is CliBool.True && string.IsNullOrWhiteSpace(options.AppInsightsConnString))
            {
                _logger.LogError("Invalid Application Insights connection string provided.");
                return false;
            }

            if (options.OpenTelemetryEnabled is CliBool.True && string.IsNullOrWhiteSpace(options.OpenTelemetryEndpoint))
            {
                _logger.LogError("Invalid OTEL endpoint provided.");
                return false;
            }

            ApplicationInsightsOptions applicationInsightsOptions = new(
                Enabled: options.AppInsightsEnabled is CliBool.True ? true : false,
                ConnectionString: options.AppInsightsConnString
            );

            OpenTelemetryOptions openTelemetryOptions = new(
                Enabled: options.OpenTelemetryEnabled is CliBool.True ? true : false,
                Endpoint: options.OpenTelemetryEndpoint,
                Headers: options.OpenTelemetryHeaders,
                ExporterProtocol: options.OpenTelemetryExportProtocol,
                ServiceName: options.OpenTelemetryServiceName
            );

            runtimeConfig = runtimeConfig with
            {
                Runtime = runtimeConfig.Runtime with
                {
                    Telemetry = runtimeConfig.Runtime.Telemetry is null
                        ? new TelemetryOptions(ApplicationInsights: applicationInsightsOptions, OpenTelemetry: openTelemetryOptions)
                        : runtimeConfig.Runtime.Telemetry with { ApplicationInsights = applicationInsightsOptions, OpenTelemetry = openTelemetryOptions }
                }
            };
            runtimeConfig = runtimeConfig with
            {
                Runtime = runtimeConfig.Runtime with
                {
                    Telemetry = runtimeConfig.Runtime.Telemetry is null
                        ? new TelemetryOptions(ApplicationInsights: applicationInsightsOptions)
                        : runtimeConfig.Runtime.Telemetry with { ApplicationInsights = applicationInsightsOptions }
                }
            };

            return WriteRuntimeConfigToFile(runtimeConfigFile, runtimeConfig, fileSystem);
        }

        /// <summary>
        /// Attempts to update the Azure Key Vault configuration options based on the provided values.
        /// Validates that any user-provided parameter value is valid and updates the runtime configuration accordingly.
        /// </summary>
        /// <param name="options">The configuration options provided by the user.</param>
        /// <param name="runtimeConfig">The runtime configuration to be updated.</param>
        /// <returns>True if the Azure Key Vault options were successfully configured; otherwise, false.</returns>
        private static bool TryUpdateConfiguredAzureKeyVaultOptions(
            ConfigureOptions options,
            [NotNullWhen(true)] ref RuntimeConfig runtimeConfig)
        {
            try
            {
                AzureKeyVaultOptions? updatedAzureKeyVaultOptions = runtimeConfig.AzureKeyVault;
                AKVRetryPolicyOptions? updatedRetryPolicyOptions = updatedAzureKeyVaultOptions?.RetryPolicy;

                // Azure Key Vault Endpoint
                if (options.AzureKeyVaultEndpoint is not null)
                {
                    updatedAzureKeyVaultOptions = updatedAzureKeyVaultOptions is not null
                        ? updatedAzureKeyVaultOptions with { Endpoint = options.AzureKeyVaultEndpoint }
                        : new AzureKeyVaultOptions { Endpoint = options.AzureKeyVaultEndpoint };
                    _logger.LogInformation("Updated RuntimeConfig with azure-key-vault.endpoint as '{endpoint}'", options.AzureKeyVaultEndpoint);
                }

                // Retry Policy Mode
                if (options.AzureKeyVaultRetryPolicyMode is not null)
                {
                    updatedRetryPolicyOptions = updatedRetryPolicyOptions is not null
                        ? updatedRetryPolicyOptions with { Mode = options.AzureKeyVaultRetryPolicyMode.Value, UserProvidedMode = true }
                        : new AKVRetryPolicyOptions { Mode = options.AzureKeyVaultRetryPolicyMode.Value, UserProvidedMode = true };
                    _logger.LogInformation("Updated RuntimeConfig with azure-key-vault.retry-policy.mode as '{mode}'", options.AzureKeyVaultRetryPolicyMode.Value);
                }

                // Retry Policy Max Count
                if (options.AzureKeyVaultRetryPolicyMaxCount is not null)
                {
                    if (options.AzureKeyVaultRetryPolicyMaxCount.Value < 1)
                    {
                        _logger.LogError("Failed to update azure-key-vault.retry-policy.max-count. Value must be at least 1.");
                        return false;
                    }

                    updatedRetryPolicyOptions = updatedRetryPolicyOptions is not null
                        ? updatedRetryPolicyOptions with { MaxCount = options.AzureKeyVaultRetryPolicyMaxCount.Value, UserProvidedMaxCount = true }
                        : new AKVRetryPolicyOptions { MaxCount = options.AzureKeyVaultRetryPolicyMaxCount.Value, UserProvidedMaxCount = true };
                    _logger.LogInformation("Updated RuntimeConfig with azure-key-vault.retry-policy.max-count as '{maxCount}'", options.AzureKeyVaultRetryPolicyMaxCount.Value);
                }

                // Retry Policy Delay Seconds
                if (options.AzureKeyVaultRetryPolicyDelaySeconds is not null)
                {
                    if (options.AzureKeyVaultRetryPolicyDelaySeconds.Value < 1)
                    {
                        _logger.LogError("Failed to update azure-key-vault.retry-policy.delay-seconds. Value must be at least 1.");
                        return false;
                    }

                    updatedRetryPolicyOptions = updatedRetryPolicyOptions is not null
                        ? updatedRetryPolicyOptions with { DelaySeconds = options.AzureKeyVaultRetryPolicyDelaySeconds.Value, UserProvidedDelaySeconds = true }
                        : new AKVRetryPolicyOptions { DelaySeconds = options.AzureKeyVaultRetryPolicyDelaySeconds.Value, UserProvidedDelaySeconds = true };
                    _logger.LogInformation("Updated RuntimeConfig with azure-key-vault.retry-policy.delay-seconds as '{delaySeconds}'", options.AzureKeyVaultRetryPolicyDelaySeconds.Value);
                }

                // Retry Policy Max Delay Seconds
                if (options.AzureKeyVaultRetryPolicyMaxDelaySeconds is not null)
                {
                    if (options.AzureKeyVaultRetryPolicyMaxDelaySeconds.Value < 1)
                    {
                        _logger.LogError("Failed to update azure-key-vault.retry-policy.max-delay-seconds. Value must be at least 1.");
                        return false;
                    }

                    updatedRetryPolicyOptions = updatedRetryPolicyOptions is not null
                        ? updatedRetryPolicyOptions with { MaxDelaySeconds = options.AzureKeyVaultRetryPolicyMaxDelaySeconds.Value, UserProvidedMaxDelaySeconds = true }
                        : new AKVRetryPolicyOptions { MaxDelaySeconds = options.AzureKeyVaultRetryPolicyMaxDelaySeconds.Value, UserProvidedMaxDelaySeconds = true };
                    _logger.LogInformation("Updated RuntimeConfig with azure-key-vault.retry-policy.max-delay-seconds as '{maxDelaySeconds}'", options.AzureKeyVaultRetryPolicyMaxDelaySeconds.Value);
                }

                // Retry Policy Network Timeout Seconds
                if (options.AzureKeyVaultRetryPolicyNetworkTimeoutSeconds is not null)
                {
                    if (options.AzureKeyVaultRetryPolicyNetworkTimeoutSeconds.Value < 1)
                    {
                        _logger.LogError("Failed to update azure-key-vault.retry-policy.network-timeout-seconds. Value must be at least 1.");
                        return false;
                    }

                    updatedRetryPolicyOptions = updatedRetryPolicyOptions is not null
                        ? updatedRetryPolicyOptions with { NetworkTimeoutSeconds = options.AzureKeyVaultRetryPolicyNetworkTimeoutSeconds.Value, UserProvidedNetworkTimeoutSeconds = true }
                        : new AKVRetryPolicyOptions { NetworkTimeoutSeconds = options.AzureKeyVaultRetryPolicyNetworkTimeoutSeconds.Value, UserProvidedNetworkTimeoutSeconds = true };
                    _logger.LogInformation("Updated RuntimeConfig with azure-key-vault.retry-policy.network-timeout-seconds as '{networkTimeoutSeconds}'", options.AzureKeyVaultRetryPolicyNetworkTimeoutSeconds.Value);
                }

                // Update Azure Key Vault options with retry policy if retry policy was modified
                if (updatedRetryPolicyOptions is not null)
                {
                    updatedAzureKeyVaultOptions = updatedAzureKeyVaultOptions is not null
                        ? updatedAzureKeyVaultOptions with { RetryPolicy = updatedRetryPolicyOptions }
                        : new AzureKeyVaultOptions { RetryPolicy = updatedRetryPolicyOptions };
                }

                // Update runtime config if Azure Key Vault options were modified
                if (updatedAzureKeyVaultOptions is not null)
                {
                    runtimeConfig = runtimeConfig with { AzureKeyVault = updatedAzureKeyVaultOptions };
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to update RuntimeConfig.AzureKeyVault with exception message: {exceptionMessage}.", ex.Message);
                return false;
            }
        }
    }
}
