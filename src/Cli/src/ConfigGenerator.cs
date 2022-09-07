using System.Collections;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using static Cli.Utils;
using PermissionOperation = Azure.DataApiBuilder.Config.PermissionOperation;

namespace Cli
{
    /// <summary>
    /// Contains the methods for Initializing the config file and Adding/Updating Entities.
    /// </summary>
    public class ConfigGenerator
    {
        /// <summary>
        /// This method will generate the initial config with databaseType and connection-string.
        /// </summary>
        public static bool TryGenerateConfig(InitOptions options)
        {
            if (!TryCreateRuntimeConfig(options, out string runtimeConfigJson))
            {
                Console.Error.Write($"Failed to create the runtime config file.");
                return false;
            }

            if (!TryGetConfigFileBasedOnCliPrecedence(options.Config, out string runtimeConfigFile))
            {
                runtimeConfigFile = RuntimeConfigPath.DefaultName;
                Console.WriteLine($"Creating a new config file: {runtimeConfigFile}");
            }

            return WriteJsonContentToFile(runtimeConfigFile, runtimeConfigJson);
        }

        /// <summary>
        /// Create a runtime config json string.
        /// </summary>
        /// <param name="options">Init options</param>
        /// <param name="runtimeConfigJson">Output runtime config json.</param>
        /// <returns>True on success. False otherwise.</returns>
        public static bool TryCreateRuntimeConfig(InitOptions options, out string runtimeConfigJson)
        {
            runtimeConfigJson = string.Empty;

            DatabaseType dbType = options.DatabaseType;
            DataSource dataSource = new(dbType)
            {
                ConnectionString = options.ConnectionString
            };

            CosmosDbOptions? cosmosDbOptions = null;
            MsSqlOptions? msSqlOptions = null;
            MySqlOptions? mySqlOptions = null;
            PostgreSqlOptions? postgreSqlOptions = null;

            switch (dbType)
            {
                case DatabaseType.cosmos:
                    string? cosmosDatabase = options.CosmosDatabase;
                    string? cosmosContainer = options.CosmosContainer;
                    string? graphQLSchemaPath = options.GraphQLSchemaPath;
                    if (string.IsNullOrEmpty(cosmosDatabase) || string.IsNullOrEmpty(graphQLSchemaPath))
                    {
                        Console.WriteLine($"Provide all the mandatory options for CosmosDB: --cosmos-database, and --graphql-schema");
                        return false;
                    }

                    cosmosDbOptions = new CosmosDbOptions(cosmosDatabase, cosmosContainer, graphQLSchemaPath, GraphQLSchema: null);
                    break;

                case DatabaseType.mssql:
                    msSqlOptions = new MsSqlOptions();
                    break;

                case DatabaseType.mysql:
                    mySqlOptions = new MySqlOptions();
                    break;

                case DatabaseType.postgresql:
                    postgreSqlOptions = new PostgreSqlOptions();
                    break;

                default:
                    Console.WriteLine($"DatabaseType: ${dbType} not supported.Please provide a valid database-type.");
                    return false;
            }

            RuntimeConfig runtimeConfig = new(
                Schema: RuntimeConfig.SCHEMA,
                DataSource: dataSource,
                CosmosDb: cosmosDbOptions,
                MsSql: msSqlOptions,
                PostgreSql: postgreSqlOptions,
                MySql: mySqlOptions,
                RuntimeSettings: GetDefaultGlobalSettings(
                    dbType,
                    options.HostMode,
                    options.CorsOrigin,
                    devModeDefaultAuth: GetDevModeDefaultAuth(options.DevModeDefaultAuth)),
                Entities: new Dictionary<string, Entity>());

            runtimeConfigJson = JsonSerializer.Serialize(runtimeConfig, GetSerializationOptions());
            return true;
        }

        /// <summary>
        /// Helper method to parse the devModeDefaultAuth string into its corresponding boolean representation.
        /// </summary>
        /// <param name="devModeDefaultAuth">string to be parsed into bool value.</param>
        /// <returns></returns>
        /// <exception cref="Exception">throws exception if string is not null and cannot be parsed into a bool value.</exception>
        private static bool? GetDevModeDefaultAuth(string? devModeDefaultAuth)
        {
            if (devModeDefaultAuth is null)
            {
                return null;
            }

            if (bool.TryParse(devModeDefaultAuth, out bool parsedBoolVar))
            {
                return parsedBoolVar;
            }

            throw new Exception($"{devModeDefaultAuth} is an invalid value for the property authenticate-devmode-requests." +
                $" It can only assume boolean values true/false.");
        }

        /// <summary>
        /// This method will add a new Entity with the given REST and GraphQL endpoints, source, and permissions.
        /// It also supports fields that needs to be included or excluded for a given role and operation.
        /// </summary>
        public static bool TryAddEntityToConfigWithOptions(AddOptions options)
        {
            if (!TryGetConfigFileBasedOnCliPrecedence(options.Config, out string runtimeConfigFile))
            {
                return false;
            }

            if (!TryReadRuntimeConfig(runtimeConfigFile, out string runtimeConfigJson))
            {
                Console.Error.Write($"Failed to read the config file: {runtimeConfigFile}.");
                return false;
            }

            if (!TryAddNewEntity(options, ref runtimeConfigJson))
            {
                Console.Error.Write("Failed to add a new entity.");
                return false;
            }

            return WriteJsonContentToFile(runtimeConfigFile, runtimeConfigJson);
        }

        /// <summary>
        /// Add new entity to runtime config json. The function will add new entity to runtimeConfigJson string.
        /// On successful return of the function, runtimeConfigJson will be modified.
        /// </summary>
        /// <param name="options">AddOptions.</param>
        /// <param name="runtimeConfigJson">Json string of existing runtime config. This will be modified on successful return.</param>
        /// <returns>True on success. False otherwise.</returns>
        public static bool TryAddNewEntity(AddOptions options, ref string runtimeConfigJson)
        {
            // Deserialize the json string to RuntimeConfig object.
            //
            RuntimeConfig? runtimeConfig;
            try
            {
                runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(runtimeConfigJson, GetSerializationOptions());
                if (runtimeConfig is null)
                {
                    throw new Exception("Failed to parse the runtime config file.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed with exception: {e}.");
                return false;
            }

            // If entity exist, we cannot add. Just exit.
            //
            if (runtimeConfig!.Entities.ContainsKey(options.Entity))
            {
                Console.WriteLine($"WARNING: Entity-{options.Entity} is already present. No new changes are added to Config.");
                return false;
            }

            Policy? policy = GetPolicyForOperation(options.PolicyRequest, options.PolicyDatabase);
            Field? field = GetFieldsForOperation(options.FieldsToInclude, options.FieldsToExclude);

            PermissionSetting[]? permissionSettings = ParsePermission(options.Permissions, policy, field);
            if (permissionSettings is null)
            {
                Console.Error.WriteLine("Please add permission in the following format. --permissions \"<<role>>:<<actions>>\"");
                return false;
            }

            // Create new entity.
            //
            Entity entity = new(
                options.Source,
                GetRestDetails(options.RestRoute),
                GetGraphQLDetails(options.GraphQLType),
                permissionSettings,
                Relationships: null,
                Mappings: null);

            // Add entity to existing runtime config.
            //
            runtimeConfig.Entities.Add(options.Entity, entity);

            // Serialize runtime config to json string
            //
            runtimeConfigJson = JsonSerializer.Serialize(runtimeConfig, GetSerializationOptions());

            return true;
        }

        /// <summary>
        /// Parse permission string to create PermissionSetting array.
        /// </summary>
        /// <param name="permissions">Permission input string as IEnumerable.</param>
        /// <param name="policy">policy to add for this permission.</param>
        /// <param name="fields">fields to include and exclude for this permission.</param>
        /// <returns></returns>
        public static PermissionSetting[]? ParsePermission(IEnumerable<string> permissions, Policy? policy, Field? fields)
        {
            // Getting Role and Operations from permission string
            string? role, operations;
            if (!TryGetRoleAndOperationFromPermission(permissions, out role, out operations))
            {
                Console.Error.Write($"Failed to fetch the role and operation from the given permission string: {string.Join(":", permissions.ToArray())}.");
                return null;
            }

            // Check if provided operations are valid
            if (!VerifyOperations(operations!.Split(",")))
            {
                return null;
            }

            PermissionSetting[] permissionSettings = new PermissionSetting[]
            {
                CreatePermissions(role!, operations!, policy, fields)
            };

            return permissionSettings;
        }

        /// <summary>
        /// This method will update an existing Entity with the given REST and GraphQL endpoints, source, and permissions.
        /// It also supports updating fields that need to be included or excluded for a given role and operation.
        /// </summary>
        public static bool TryUpdateEntityWithOptions(UpdateOptions options)
        {
            if (!TryGetConfigFileBasedOnCliPrecedence(options.Config, out string runtimeConfigFile))
            {
                return false;
            }

            if (!TryReadRuntimeConfig(runtimeConfigFile, out string runtimeConfigJson))
            {
                Console.Error.Write($"Failed to read the config file: {runtimeConfigFile}.");
                return false;
            }

            if (!TryUpdateExistingEntity(options, ref runtimeConfigJson))
            {
                Console.Error.Write($"Failed to update the Entity: {options.Entity}.");
                return false;
            }

            return WriteJsonContentToFile(runtimeConfigFile, runtimeConfigJson);
        }

        /// <summary>
        /// Update an existing entity in the runtime config json.
        /// On successful return of the function, runtimeConfigJson will be modified.
        /// </summary>
        /// <param name="options">UpdateOptions.</param>
        /// <param name="runtimeConfigJson">Json string of existing runtime config. This will be modified on successful return.</param>
        /// <returns>True on success. False otherwise.</returns>
        public static bool TryUpdateExistingEntity(UpdateOptions options, ref string runtimeConfigJson)
        {
            // Deserialize the json string to RuntimeConfig object.
            //
            RuntimeConfig? runtimeConfig;
            try
            {
                runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(runtimeConfigJson, GetSerializationOptions());
                if (runtimeConfig is null)
                {
                    throw new Exception("Failed to parse the runtime config file.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed with exception: {e}.");
                return false;
            }

            // Check if Entity is present
            //
            Entity? entity;
            if (!runtimeConfig.Entities.TryGetValue(options.Entity, out entity))
            {
                Console.WriteLine($"Entity:{options.Entity} not found. Please add the entity first.");
                return false;
            }

            object updatedSource = options.Source is null ? entity!.Source : options.Source;
            object? updatedRestDetails = options.RestRoute is null ? entity!.Rest : GetRestDetails(options.RestRoute);
            object? updatedGraphqlDetails = options.GraphQLType is null ? entity!.GraphQL : GetGraphQLDetails(options.GraphQLType);
            PermissionSetting[]? updatedPermissions = entity!.Permissions;
            Dictionary<string, Relationship>? updatedRelationships = entity.Relationships;
            Dictionary<string, string>? updatedMappings = entity.Mappings;
            Policy? updatedPolicy = GetPolicyForOperation(options.PolicyRequest, options.PolicyDatabase);
            Field? updatedFields = GetFieldsForOperation(options.FieldsToInclude, options.FieldsToExclude);

            if (options.Permissions is not null && options.Permissions.Any())
            {
                // Get the Updated Permission Settings
                //
                updatedPermissions = GetUpdatedPermissionSettings(entity, options.Permissions, updatedPolicy, updatedFields);

                if (updatedPermissions is null)
                {
                    Console.WriteLine($"Failed to update permissions.");
                    return false;
                }
            }
            else
            {

                if (options.FieldsToInclude is not null && options.FieldsToInclude.Any()
                    || options.FieldsToExclude is not null && options.FieldsToExclude.Any())
                {
                    Console.WriteLine($"--permissions is mandatory with --fields.include and --fields.exclude.");
                    return false;
                }

                if (options.PolicyRequest is not null || options.PolicyDatabase is not null)
                {
                    Console.WriteLine($"--permissions is mandatory with --policy-request and --policy-database.");
                    return false;
                }
            }

            if (options.Relationship is not null)
            {
                if (!VerifyCanUpdateRelationship(runtimeConfig, options.Cardinality, options.TargetEntity))
                {
                    return false;
                }

                if (updatedRelationships is null)
                {
                    updatedRelationships = new();
                }

                Relationship? new_relationship = CreateNewRelationshipWithUpdateOptions(options);
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

            runtimeConfig.Entities[options.Entity] = new Entity(updatedSource,
                                                                updatedRestDetails,
                                                                updatedGraphqlDetails,
                                                                updatedPermissions,
                                                                updatedRelationships,
                                                                updatedMappings);
            runtimeConfigJson = JsonSerializer.Serialize(runtimeConfig, GetSerializationOptions());
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
        /// <returns> On failure, returns null. Else updated PermissionSettings array will be returned.</returns>
        private static PermissionSetting[]? GetUpdatedPermissionSettings(Entity entityToUpdate,
                                                                        IEnumerable<string> permissions,
                                                                        Policy? policy,
                                                                        Field? fields)
        {
            string? newRole, newOperations;

            // Parse role and operations from the permissions string
            //
            if (!TryGetRoleAndOperationFromPermission(permissions, out newRole, out newOperations))
            {
                Console.Error.Write($"Failed to fetch the role and operation from the given permission string: {permissions}.");
                return null;
            }

            List<PermissionSetting> updatedPermissionsList = new();
            string[] newOperationArray = newOperations!.Split(",");

            if (!VerifyOperations(newOperationArray))
            {
                return null;
            }

            bool role_found = false;
            // Loop through the current permissions
            foreach (PermissionSetting permission in entityToUpdate.Permissions)
            {
                // Find the role that needs to be updated
                if (permission.Role.Equals(newRole!))
                {
                    role_found = true;
                    if (newOperationArray.Length is 1 && WILDCARD.Equals(newOperationArray[0]))
                    {
                        // If the user inputs WILDCARD as operation, we overwrite the existing operations.
                        updatedPermissionsList.Add(CreatePermissions(newRole!, WILDCARD, policy, fields));
                    }
                    else
                    {
                        // User didn't use WILDCARD, and wants to update some of the operations.
                        IDictionary<Operation, PermissionOperation> existingOperations = ConvertOperationArrayToIEnumerable(permission.Operations);

                        // Merge existing operations with new operations
                        object[] updatedOperationArray = GetUpdatedOperationArray(newOperationArray, policy, fields, existingOperations);

                        updatedPermissionsList.Add(new PermissionSetting(newRole, updatedOperationArray));
                    }
                }
                else
                {
                    updatedPermissionsList.Add(permission);
                }
            }

            // if the role we are trying to update is not found, we create a new one
            // and add it to permissionSettings list.
            if (!role_found)
            {
                updatedPermissionsList.Add(CreatePermissions(newRole!, newOperations!, policy, fields));
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
        private static object[] GetUpdatedOperationArray(string[] newOperations,
                                                        Policy? newPolicy,
                                                        Field? newFields,
                                                        IDictionary<Operation, PermissionOperation> existingOperations)
        {
            Dictionary<Operation, PermissionOperation> updatedOperations = new();

            Policy? existingPolicy = null;
            Field? existingFields = null;

            // Adding the new operations in the updatedOperationList
            foreach (string operation in newOperations)
            {
                // Getting existing Policy and Fields
                if (TryConvertOperationNameToOperation(operation, out Operation op))
                {
                    if (existingOperations.ContainsKey(op))
                    {
                        existingPolicy = existingOperations[op].Policy;
                        existingFields = existingOperations[op].Fields;
                    }

                    // Checking if Policy and Field update is required
                    Policy? updatedPolicy = newPolicy is null ? existingPolicy : newPolicy;
                    Field? updatedFields = newFields is null ? existingFields : newFields;

                    updatedOperations.Add(op, new PermissionOperation(op, updatedPolicy, updatedFields));
                }
            }

            // Looping through existing operations
            foreach (KeyValuePair<Operation, PermissionOperation> operation in existingOperations)
            {
                // if any existing operation doesn't require update, it is added as it is.
                if (!updatedOperations.ContainsKey(operation.Key))
                {
                    updatedOperations.Add(operation.Key, operation.Value);
                }
            }

            // Convert operation object to an array.
            // If there is no policy or field for this operation, it will be converted to a string.
            // Otherwise, it is added as operation object.
            //
            ArrayList updatedOperationArray = new();
            foreach (PermissionOperation updatedOperation in updatedOperations.Values)
            {
                if (updatedOperation.Policy is null && updatedOperation.Fields is null)
                {
                    updatedOperationArray.Add(updatedOperation.Name.ToString());
                }
                else
                {
                    updatedOperationArray.Add(updatedOperation);
                }
            }

            return updatedOperationArray.ToArray();
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
            if (runtimeConfig.DataSource.DatabaseType.Equals(DatabaseType.cosmos))
            {
                Console.Error.WriteLine("Adding/updating Relationships is currently not supported in CosmosDB.");
                return false;
            }

            // Checking if both cardinality and targetEntity is provided.
            //
            if (cardinality is null || targetEntity is null)
            {
                Console.WriteLine("cardinality and target entity is mandatory to update/add a relationship.");
                return false;
            }

            // Both the source entity and target entity needs to present in config to establish relationship.
            //
            if (!runtimeConfig.Entities.ContainsKey(targetEntity))
            {
                Console.WriteLine($"Entity:{targetEntity} is not present. Relationship cannot be added.");
                return false;
            }

            // Check if provided value of cardinality is present in the enum.
            //
            if (!string.Equals(cardinality, Cardinality.One.ToString(), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(cardinality, Cardinality.Many.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Failed to parse the given cardinality : {cardinality}. Supported values are one/many.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// This Method will create a new Relationship Object based on the given UpdateOptions.
        /// </summary>
        /// <param name="options">update options </param>
        /// <returns>Returns a Relationship Object</returns>
        public static Relationship? CreateNewRelationshipWithUpdateOptions(UpdateOptions options)
        {
            string[]? updatedSourceFields = null;
            string[]? updatedTargetFields = null;
            string[]? updatedLinkingSourceFields = options.LinkingSourceFields is null || !options.LinkingSourceFields.Any() ? null : options.LinkingSourceFields.ToArray();
            string[]? updatedLinkingTargetFields = options.LinkingTargetFields is null || !options.LinkingTargetFields.Any() ? null : options.LinkingTargetFields.ToArray();

            Cardinality updatedCardinality = Enum.Parse<Cardinality>(options.Cardinality!, ignoreCase: true);

            if (options.RelationshipFields is not null && options.RelationshipFields.Any())
            {
                // Getting source and target fields from mapping fields
                //
                if (options.RelationshipFields.Count() != 2)
                {
                    Console.WriteLine("Please provide the --relationship.fields in the correct format using ':' between source and target fields.");
                    return null;
                }

                updatedSourceFields = options.RelationshipFields.ElementAt(0).Split(",");
                updatedTargetFields = options.RelationshipFields.ElementAt(1).Split(",");
            }

            return new Relationship(updatedCardinality,
                                    options.TargetEntity!,
                                    updatedSourceFields,
                                    updatedTargetFields,
                                    options.LinkingObject,
                                    updatedLinkingSourceFields,
                                    updatedLinkingTargetFields);
        }

        /// <summary>
        /// This method will try starting the engine.
        /// it will use the config provided by the user, else will look for the default config.
        /// </summary>
        public static bool TryStartEngineWithOptions(StartOptions options)
        {
            if (!TryGetConfigFileBasedOnCliPrecedence(options.Config, out string runtimeConfigFile))
            {
                Console.Error.WriteLine("Config not provided and default config file doesn't exist.");
                return false;
            }

            /// This will start the runtime engine with project name and config file.
            string[] args = new string[] { "--" + nameof(RuntimeConfigPath.ConfigFileName), runtimeConfigFile };
            return Azure.DataApiBuilder.Service.Program.StartEngine(args);
        }
    }
}
