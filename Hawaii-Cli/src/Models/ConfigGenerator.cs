using System.Text.Json;
using Azure.DataGateway.Config;
using static Hawaii.Cli.Models.Utils;
using Action = Azure.DataGateway.Config.Action;

namespace Hawaii.Cli.Models
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
            string runtimeConfigJson;
            if (!TryCreateRuntimeConfig(options, out runtimeConfigJson))
            {
                Console.Error.Write($"Failed to create the runtime config file.");
                return false;
            }

            string file = $"{options.Name}.json";

            return WriteJsonContentToFile(file, runtimeConfigJson);
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
                        Console.WriteLine($"Please provide the mandatory options for CosmosDB: --cosmos-database, --graphql-schema");
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
                RuntimeSettings: GetDefaultGlobalSettings(dbType, options.HostMode),
                Entities: new Dictionary<string, Entity>());

            runtimeConfigJson = JsonSerializer.Serialize(runtimeConfig, GetSerializationOptions());
            return true;
        }

        /// <summary>
        /// This method will add a new Entity with the given REST and GraphQL endpoints, source, and permissions.
        /// It also supports fields that needs to be included or excluded for a given role and action.
        /// </summary>
        public static bool TryAddEntityToConfigWithOptions(AddOptions options)
        {
            string file = $"{options.Name}.json";

            string runtimeConfigJson;
            if (!TryReadRuntimeConfig(file, out runtimeConfigJson))
            {
                Console.Error.Write($"Failed to read the config file: {file}.");
                return false;
            }

            if (!TryAddNewEntity(options, ref runtimeConfigJson))
            {
                Console.Error.Write("Failed to add a new entity.");
                return false;
            }

            return WriteJsonContentToFile(file, runtimeConfigJson);
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

            Policy? policy = GetPolicyForAction(options.PolicyRequest, options.PolicyDatabase);
            Field? field = GetFieldsForAction(options.FieldsToInclude, options.FieldsToExclude);

            PermissionSetting[]? permissionSettings = ParsePermission(options.Permissions, policy, field);
            if (permissionSettings is null)
            {
                Console.Error.WriteLine("Please add permission in the following format. --permission \"<<role>>:<<actions>>\"");
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
        /// <param name="permissions">Permission string input.</param>
        /// <param name="fieldsToInclude">fields to include for this permission.</param>
        /// <param name="fieldsToExclude">fields to exclude for this permission.</param>
        /// <returns></returns>
        public static PermissionSetting[]? ParsePermission(string permissions, Policy? policy, Field? fields)
        {
            // Getting Role and Actions from permission string
            //
            string? role, actions;
            if (!TryGetRoleAndActionFromPermissionString(permissions, out role, out actions))
            {
                Console.Error.Write($"Failed to fetch the role and action from the given permission string: {permissions}.");
                return null;
            }

            // Check if provided actions are valid
            if (!VerifyActions(actions!.Split(",")))
            {
                return null;
            }

            PermissionSetting[] permissionSettings = new PermissionSetting[]
            {
                CreatePermissions(role!, actions!, policy, fields)
            };

            return permissionSettings;
        }

        /// <summary>
        /// This method will update an existing Entity with the given REST and GraphQL endpoints, source, and permissions.
        /// It also supports updating fields that need to be included or excluded for a given role and action.
        /// </summary>
        public static bool TryUpdateEntityWithOptions(UpdateOptions options)
        {
            string file = $"{options.Name}.json";

            string runtimeConfigJson;
            if (!TryReadRuntimeConfig(file, out runtimeConfigJson))
            {
                Console.Error.Write($"Failed to read the config file: {file}.");
                return false;
            }

            if (!TryUpdateExistingEntity(options, ref runtimeConfigJson))
            {
                Console.Error.Write($"Failed to update the Entity: {options.Entity}.");
                return false;
            }

            return WriteJsonContentToFile(file, runtimeConfigJson);
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
            string? source = options.Source;
            string? rest = options.RestRoute;
            string? graphQL = options.GraphQLType;
            string? permissions = options.Permissions;
            string? fieldsToInclude = options.FieldsToInclude;
            string? fieldsToExclude = options.FieldsToExclude;
            string? policyRequest = options.PolicyRequest;
            string? policyDatabase = options.PolicyDatabase;
            string? relationship = options.Relationship;
            string? cardinality = options.Cardinality;
            string? targetEntity = options.TargetEntity;

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
            bool IsEntityPresent = runtimeConfig.Entities.TryGetValue(options.Entity, out entity);
            if (!IsEntityPresent)
            {
                Console.WriteLine($"Entity:{options.Entity} not found. Please add the entity first.");
                return false;
            }

            object updatedSource = source is null ? entity!.Source : source;
            object? updatedRestDetails = rest is null ? entity!.Rest : GetRestDetails(rest);
            object? updatedGraphqlDetails = graphQL is null ? entity!.GraphQL : GetGraphQLDetails(graphQL);
            PermissionSetting[]? updatedPermissions = entity!.Permissions;
            Dictionary<string, Relationship>? updatedRelationships = entity.Relationships;
            Dictionary<string, string>? updatedMappings = entity.Mappings;
            Policy? updatedPolicy = GetPolicyForAction(policyRequest, policyDatabase);
            Field? updatedFields = GetFieldsForAction(fieldsToInclude, fieldsToExclude);

            if (permissions is not null)
            {
                // Get the Updated Permission Settings
                //
                updatedPermissions = GetUpdatedPermissionSettings(entity, permissions, updatedPolicy, updatedFields);

                if (updatedPermissions is null)
                {
                    Console.WriteLine($"Failed to update permissions.");
                    return false;
                }
            }
            else
            {
                if (fieldsToInclude is not null || fieldsToExclude is not null)
                {
                    Console.WriteLine($"--permission is mandatory with --fields.include and --fields.exclude.");
                    return false;
                }

                if (policyRequest is not null || policyDatabase is not null)
                {
                    Console.WriteLine($"--permission is mandatory with --policy-request and --policy-database.");
                    return false;
                }
            }

            if (relationship is not null)
            {
                if (!VerifyCanUpdateRelationship(runtimeConfig, cardinality, targetEntity))
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

                updatedRelationships[relationship] = new_relationship;
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
        /// <param name="fieldsToInclude">fields to allow the action permission</param>
        /// <param name="fieldsToExclude">fields that will be excluded from the action permission.</param>
        /// <returns> On failure, returns null. Else updated PermissionSettings array will be returned.</returns>
        private static PermissionSetting[]? GetUpdatedPermissionSettings(Entity entityToUpdate,
                                                                        string permissions,
                                                                        Policy? policy,
                                                                        Field? fields)
        {
            string? newRole, newActions;

            // Parse role and actions from the permissions string
            //
            if (!TryGetRoleAndActionFromPermissionString(permissions, out newRole, out newActions))
            {
                Console.Error.Write($"Failed to fetch the role and action from the given permission string: {permissions}.");
                return null;
            }

            List<PermissionSetting> updatedPermissionsList = new();
            string[] newActionArray = newActions!.Split(",");

            if (!VerifyActions(newActionArray))
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
                    if (newActionArray.Length is 1 && WILDCARD.Equals(newActionArray[0]))
                    {
                        // If the user inputs WILDCARD as actions, we overwrite the existing actions.
                        //
                        updatedPermissionsList.Add(CreatePermissions(newRole!, WILDCARD, policy, fields));
                    }
                    else
                    {
                        // User didn't use WILDCARD, and wants to update some of the CRUD actions.
                        //
                        List<object> actionList = permission.Actions.ToList();
                        if (permission.Actions.Length is 1 && GetCRUDOperation((JsonElement)permission.Actions[0]) is WILDCARD)
                        {
                            // Expanding WILDCARD operation to all the CRUD operations before updating.
                            //
                            actionList = ExpandWildcardToAllCRUDActions(permission);
                        }

                        // updating the current action list
                        object[] updatedActionArray = GetUpdatedActionArray(newActionArray, policy, fields, actionList);

                        updatedPermissionsList.Add(new PermissionSetting(newRole, updatedActionArray));
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
                updatedPermissionsList.Add(CreatePermissions(newRole!, newActions!, policy, fields));
            }

            return updatedPermissionsList.ToArray();
        }

        /// <summary>
        /// This Method will expand Wildcard("*") to all the CRUD actions.
        /// It is useful in cases where we need to update some of the actions.
        /// </summary>
        /// <param name="permission">permission which needs to be updated</param>
        /// <returns>List of all the CRUD objects</returns>
        private static List<object> ExpandWildcardToAllCRUDActions(PermissionSetting permission)
        {
            List<object> actionList = new();
            Action? action = ToActionObject((JsonElement)permission.Actions[0]);

            if (action!.Policy is null && action!.Fields is null)
            {
                actionList = Enum.GetNames(typeof(CRUD)).ToList<object>();
            }
            else
            {
                foreach (string op in Enum.GetNames(typeof(CRUD)))
                {
                    actionList.Add(new Action(Name: op, Policy: action.Policy, Fields: action.Fields));
                }
            }

            return actionList;
        }

        /// <summary>
        /// Merge old and new actions into a new list. Take all new updated actions.
        /// Only add existing actions to the merged list if there is no update.
        /// </summary>
        ///
        /// <param name="newActions">action items to update received from user.</param>
        /// <param name="fieldsToInclude">fields to allow the action permission</param>
        /// <param name="fieldsToExclude">fields that will be excluded form the action permission.</param>
        /// <param name="existingActions">action items present in the config.</param>
        /// <returns>Array of updated Action objects</returns>
        private static object[] GetUpdatedActionArray(string[] newActions,
                                                        Policy? newPolicy,
                                                        Field? newFields,
                                                        List<object> existingActions)
        {
            // a new list to store merged result.
            List<object> updatedActionList = new();

            // create a hash table of new action
            HashSet<string> newActionSet = newActions.ToHashSet();

            Dictionary<string, Action> existingActionMap = GetDictionaryFromActionObjectList(existingActions);
            Policy? existingPolicy = null;
            Field? existingFields = null;
            // Adding the new Actions in the updatedActionList
            foreach (string action in newActionSet)
            {
                // Getting existing Policy and Fields
                if (existingActionMap.ContainsKey(action))
                {
                    existingPolicy = existingActionMap[action].Policy;
                    existingFields = existingActionMap[action].Fields;
                }

                // Checking if Policy and Field update is required
                Policy? updatedPolicy = newPolicy is null ? existingPolicy : newPolicy;
                Field? updatedFields = newFields is null ? existingFields : newFields;

                updatedActionList.Add(new Action(action, updatedPolicy, updatedFields));
            }

            // Looping through existing actions
            foreach (object action in existingActions)
            {
                // getting action name from action object
                string actionName = GetCRUDOperation(JsonSerializer.SerializeToElement(action));

                // if any existing action doesn't require update, it is added as it is.
                if (!newActionSet.Contains(actionName))
                {
                    updatedActionList.Add(action);
                }
            }

            return updatedActionList.ToArray();
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
            string? cardinality = options.Cardinality;
            string? targetEntity = options.TargetEntity;
            string? linkingObject = options.LinkingObject;
            string? linkingSourceFields = options.LinkingSourceFields;
            string? linkingTargetFields = options.LinkingTargetFields;
            string? mappingFields = options.MappingFields;
            string[]? updatedSourceFields = null;
            string[]? updatedTargetFields = null;
            string[]? updatedLinkingSourceFields = linkingSourceFields is null ? null : linkingSourceFields.Split(",");
            string[]? updatedLinkingTargetFields = linkingTargetFields is null ? null : linkingTargetFields.Split(",");

            Cardinality updatedCardinality = Enum.Parse<Cardinality>(cardinality!, ignoreCase: true);

            if (mappingFields is not null)
            {
                // Getting source and target fields from mapping fields
                //
                string[] mappingFieldsArray = mappingFields.Split(":");
                if (mappingFieldsArray.Length != 2)
                {
                    Console.WriteLine("Please provide the --mapping.fields in the correct format using ':' between source and target fields.");
                    return null;
                }

                updatedSourceFields = mappingFieldsArray[0].Split(",");
                updatedTargetFields = mappingFieldsArray[1].Split(",");
            }

            return new Relationship(updatedCardinality,
                                    targetEntity!,
                                    updatedSourceFields,
                                    updatedTargetFields,
                                    linkingObject,
                                    updatedLinkingSourceFields,
                                    updatedLinkingTargetFields);
        }
    }
}
