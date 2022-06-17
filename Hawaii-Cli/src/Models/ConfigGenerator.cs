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
                return false;
            }

            string file = $"{options.Name}.json";

            try
            {
                File.WriteAllText(file, runtimeConfigJson);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to Generate the config file, operation failed with exception:{e}.");
                return false;
            }

            return true;
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
                        Console.WriteLine($"Please provide all the mandatory options for CosmosDB: --cosmos-database, --graphql-schema");
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

            RuntimeConfig runtimeConfig;
            try
            {
                runtimeConfig = new RuntimeConfig(
                    Schema: RuntimeConfig.SCHEMA,
                    DataSource: dataSource,
                    CosmosDb: cosmosDbOptions,
                    MsSql: msSqlOptions,
                    PostgreSql: postgreSqlOptions,
                    MySql: mySqlOptions,
                    RuntimeSettings: GetDefaultGlobalSettings(dbType, options.HostMode),
                    Entities: new Dictionary<string, Entity>());
            }
            catch (NotSupportedException e)
            {
                Console.WriteLine($"{e}");
                return false;
            }

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
                return false;
            }

            if (!TryAddNewEntity(options, ref runtimeConfigJson))
            {
                return false;
            }

            try
            {
                File.WriteAllText(file, runtimeConfigJson);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to Generate the config file, operation failed with exception:{e}.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Add new entity to runtime config json. The function will add new entity to runtimeConfigJson string.
        /// On sucessful return of the function, runtimeConfigJson will be modified.
        /// </summary>
        /// <param name="options">AddOptions.</param>
        /// <param name="runtimeConfigJson">Json string of existing runtime config. This will be modified on successful return.</param>
        /// <returns>True on success. False otherwise.</returns>
        public static bool TryAddNewEntity(AddOptions options, ref string runtimeConfigJson)
        {
            // Deserialize the content to RuntimeConfig.
            //
            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(runtimeConfigJson, GetSerializationOptions());

            // If entity exist, we cannot add. Just exit.
            //
            if (runtimeConfig!.Entities.ContainsKey(options.Entity))
            {
                Console.WriteLine($"WARNING: Entity-{options.Entity} is already present. No new changes are added to Config.");
                return false;
            }

            PermissionSetting[]? permissionSettings = ParsePermission(options.Permissions, options.FieldsToInclude, options.FieldsToExclude);
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
        public static PermissionSetting[]? ParsePermission(string permissions, string? fieldsToInclude, string? fieldsToExclude)
        {
            // Split permission to role and actions
            //
            string[] permission_array = permissions.Split(":");
            if (permission_array.Length != 2)
            {
                return null;
            }

            string role = permission_array[0];
            string actions = permission_array[1];
            PermissionSetting[] permissionSettings = new PermissionSetting[]
            {
                CreatePermissions(role, actions, fieldsToInclude, fieldsToExclude)
            };

            return permissionSettings;
        }

        /// <summary>
        /// This method will update an existing Entity with the given REST and GraphQL endpoints, source, and permissions.
        /// It also supports adding a new relationship as well as updating an existing one.
        /// </summary>
        public static bool TryUpdateEntityWithOptions(UpdateOptions options)
        {
            string? source = options.Source;
            string? rest = options.RestRoute;
            string? graphQL = options.GraphQLType;
            string? permissions = options.Permissions;
            string? fieldsToInclude = options.FieldsToInclude;
            string? fieldsToExclude = options.FieldsToExclude;
            string? relationship = options.Relationship;
            string? cardinality = options.Cardinality;
            string? targetEntity = options.TargetEntity;
            string? linkingObject = options.LinkingObject;
            string? linkingSourceFields = options.LinkingSourceFields;
            string? linkingTargetFields = options.LinkingTargetFields;
            string? mappingFields = options.MappingFields;

            string file = $"{options.Name}.json";
            string runtimeConfigJson;
            if (!TryReadRuntimeConfig(file, out runtimeConfigJson))
            {
                return false;
            }

            // Deserialize the content to RuntimeConfig.
            //
            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(runtimeConfigJson, GetSerializationOptions());

            Entity updatedEntity = runtimeConfig!.Entities[options.Entity];
            if (source is not null)
            {
                updatedEntity = new Entity(source, updatedEntity.Rest, updatedEntity.GraphQL, updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }

            if (rest is not null)
            {
                updatedEntity = new Entity(updatedEntity.Source, GetRestDetails(rest), updatedEntity.GraphQL, updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }

            if (graphQL is not null)
            {
                updatedEntity = new Entity(updatedEntity.Source, updatedEntity.Rest, GetGraphQLDetails(graphQL), updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }

            if (permissions is not null)
            {
                string[] permission_array = permissions.Split(":");
                if (permission_array.Length is not 2)
                {
                    Console.WriteLine("Please add permission in the following format. --permission \"<<role>>:<<actions>>\"");
                    return false;
                }

                string new_role = permission_array[0];
                string new_action = permission_array[1];
                PermissionSetting? dict = Array.Find(updatedEntity.Permissions, item => item.Role == new_role);
                PermissionSetting[] updatedPermissions;
                List<PermissionSetting> permissionSettingsList = new();
                if (dict is null)
                {
                    updatedPermissions = AddNewPermissions(updatedEntity.Permissions, new_role, new_action, fieldsToInclude, fieldsToExclude);
                }
                else
                {
                    string[] new_action_elements = new_action.Split(",");
                    if (new_action_elements.Length > 1)
                    {
                        Console.WriteLine($"ERROR: we currently support updating only one action operation.");
                        return false;
                    }

                    string new_action_element = new_action;
                    foreach (PermissionSetting permission in updatedEntity.Permissions)
                    {    //Loop through current permissions for an entity.
                        if (permission.Role == new_role)
                        { // Updating an existing permission
                            string operation = GetCRUDOperation((JsonElement)permission.Actions[0]);
                            if (permission.Actions.Length == 1 && "*".Equals(operation))
                            { // if the role had only one action and that is "*"
                                if (operation == new_action_element)
                                { // if the new and old action is "*"
                                    permissionSettingsList.Add(CreatePermissions(permission.Role, "*", fieldsToInclude, fieldsToExclude));
                                }
                                else
                                { // if the new action is other than "*"
                                    List<Action> action_list = new();
                                    //looping through all the CRUD operations and updating the one which is asked
                                    foreach (string op in Enum.GetNames(typeof(CRUD)))
                                    {
                                        if (op.Equals(new_action_element))
                                        { //if the current crud operation is equal to the asked crud operation
                                            action_list.Add(GetAction(op, fieldsToInclude, fieldsToExclude));
                                        }
                                        else
                                        {    // else we just create a new node and add it with existing properties
                                            if (!JsonValueKind.String.Equals(((JsonElement)permission.Actions[0]).ValueKind))
                                            {
                                                Field? fields_dict = ToActionObject((JsonElement)permission.Actions[0])!.Fields;
                                                action_list.Add(new Action(op, Policy: null, Fields: new Field(fields_dict!.Include, fields_dict!.Exclude)));
                                            }
                                            else
                                            {
                                                action_list.Add(new Action(op, Policy: null, Fields: null));
                                            }
                                        }
                                    }

                                    permissionSettingsList.Add(new PermissionSetting(permission.Role, action_list.ToArray()));
                                }
                            }
                            else
                            {
                                if ("*".Equals(new_action_element))
                                {
                                    permissionSettingsList.Add(new PermissionSetting(permission.Role, CreateActions(new_action_element, fieldsToInclude, fieldsToExclude)));
                                }
                                else
                                {
                                    List<object> action_list = new();
                                    bool existing_action_element = false;
                                    foreach (JsonElement action_element in permission.Actions)
                                    {
                                        operation = GetCRUDOperation(action_element);
                                        if (new_action_element.Equals(operation))
                                        {
                                            action_list.Add(GetAction(operation, fieldsToInclude, fieldsToExclude));
                                            existing_action_element = true;
                                        }
                                        else
                                        {
                                            action_list.Add(action_element);
                                        }
                                    }

                                    if (!existing_action_element)
                                    {
                                        action_list.Add(GetAction(new_action_element, fieldsToInclude, fieldsToExclude));
                                    }

                                    permissionSettingsList.Add(new PermissionSetting(permission.Role, action_list.ToArray()));
                                }
                            }
                        }
                        else
                        { //Adding a new permission
                            permissionSettingsList.Add(permission);
                        }
                    }

                    updatedPermissions = permissionSettingsList.ToArray();
                }

                updatedEntity = new Entity(updatedEntity.Source, updatedEntity.Rest, updatedEntity.GraphQL, updatedPermissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }
            else
            {
                if (fieldsToInclude is not null || fieldsToExclude is not null)
                {
                    Console.WriteLine($"please provide the role and action name to apply this update to.");
                    return true;
                }
            }

            if (relationship is not null)
            {
                //if it's an existing relation
                if (updatedEntity.Relationships is not null && updatedEntity.Relationships.ContainsKey(relationship))
                {
                    Relationship currentRelationship = updatedEntity.Relationships[relationship];
                    Dictionary<string, Relationship> relationship_mapping = new();
                    Relationship updatedRelationship = currentRelationship;
                    if (cardinality is not null)
                    {
                        Cardinality cardinalityType;
                        try
                        {
                            cardinalityType = GetCardinalityTypeFromString(cardinality);
                        }
                        catch (System.NotSupportedException)
                        {
                            Console.WriteLine($"Given Cardinality: {cardinality} not supported. Currently supported options: one or many.");
                            return false;
                        }

                        updatedRelationship = new Relationship(cardinalityType, updatedRelationship.TargetEntity, updatedRelationship.SourceFields, updatedRelationship.TargetFields, updatedRelationship.LinkingObject, updatedRelationship.LinkingSourceFields, updatedRelationship.LinkingTargetFields);
                    }

                    if (targetEntity is not null)
                    {
                        if (!runtimeConfig.Entities.ContainsKey(targetEntity))
                        {
                            Console.WriteLine($"Entity:{targetEntity} is not present. No new changes are added to Config.");
                            return false;
                        }

                        updatedRelationship = new Relationship(updatedRelationship.Cardinality, targetEntity, updatedRelationship.SourceFields, updatedRelationship.TargetFields, updatedRelationship.LinkingObject, updatedRelationship.LinkingSourceFields, updatedRelationship.LinkingTargetFields);
                    }

                }
                else
                {    // if it's a new relationship
                    if (cardinality is not null && targetEntity is not null)
                    {
                        Dictionary<string, Relationship> relationship_mapping = updatedEntity.Relationships is null ? new Dictionary<string, Relationship>() : updatedEntity.Relationships;
                        Cardinality cardinalityType;
                        try
                        {
                            cardinalityType = GetCardinalityTypeFromString(cardinality);
                        }
                        catch (System.NotSupportedException)
                        {
                            Console.WriteLine($"Given Cardinality: {cardinality} not supported. Currently supported options: one or many.");
                            return false;
                        }

                        if (!runtimeConfig.Entities.ContainsKey(targetEntity))
                        {
                            Console.WriteLine($"Entity:{targetEntity} is not present. No new changes are added to Config.");
                            return false;
                        }

                        relationship_mapping.Add(relationship, new Relationship(cardinalityType, targetEntity,
                                                                                    SourceFields: null, TargetFields: null,
                                                                                    LinkingObject: null, LinkingSourceFields: null,
                                                                                    LinkingTargetFields: null));

                        updatedEntity = new Entity(updatedEntity.Source, updatedEntity.Rest, updatedEntity.GraphQL, updatedEntity.Permissions, relationship_mapping, updatedEntity.Mappings);
                    }
                    else
                    {
                        Console.WriteLine($"ERROR: For adding a new relationship following options are mandatory: --relationship, --cardinality, --target.entity.");
                        return false;
                    }
                }

                if (mappingFields is not null)
                {
                    string[]? sourceAndTargetFields = null;
                    string[]? sourceFields = null;
                    string[]? targetFields = null;

                    sourceAndTargetFields = mappingFields.Split(":");
                    if (sourceAndTargetFields.Length is not 2)
                    {
                        Console.WriteLine($"ERROR: Please use correct format for --mappings.fields, It should be \"<<source.fields>>:<<target.fields>>\".");
                        return false;
                    }

                    sourceFields = sourceAndTargetFields[0].Split(",");
                    targetFields = sourceAndTargetFields[1].Split(",");

                    Relationship updatedRelationship = updatedEntity.Relationships![relationship];
                    updatedRelationship = new Relationship(updatedRelationship.Cardinality, updatedRelationship.TargetEntity,
                                                            sourceFields, targetFields, updatedRelationship.LinkingObject,
                                                            updatedRelationship.LinkingSourceFields, updatedRelationship.LinkingTargetFields);

                    updatedEntity.Relationships[relationship] = updatedRelationship;
                }

                if (linkingObject is not null && linkingSourceFields is not null && linkingTargetFields is not null)
                {
                    string[] linkingSourceFieldsArray = linkingSourceFields.Split(",");
                    string[] linkingTargetFieldsArray = linkingTargetFields.Split(",");

                    Relationship updatedRelationship = updatedEntity.Relationships![relationship];
                    updatedRelationship = new Relationship(updatedRelationship.Cardinality, updatedRelationship.TargetEntity,
                                                            updatedRelationship.SourceFields, updatedRelationship.TargetFields,
                                                            linkingObject, linkingSourceFieldsArray, linkingTargetFieldsArray);

                    updatedEntity.Relationships[relationship] = updatedRelationship;
                }
                else if (linkingObject is not null || linkingSourceFields is not null || linkingTargetFields is not null)
                {
                    Console.WriteLine($"ERROR: Please provide --linking.object, --linking.source.fields, and --linking.target.fields to add the linking object.");
                    return false;
                }

            }

            runtimeConfig.Entities[options.Entity] = updatedEntity;

            runtimeConfigJson = JsonSerializer.Serialize(runtimeConfig, GetSerializationOptions());
            File.WriteAllText(file, runtimeConfigJson);

            return true;
        }

        /// <summary>
        /// Try to read and deserialize runtime config from a file.
        /// </summary>
        /// <param name="file">File path.</param>
        /// <param name="runtimeConfig">Runtime config output. On failure, this will be null.</param>
        /// <returns>True on success. On failure, return false and runtimeConfig will be set to null.</returns>
        private static bool TryReadRuntimeConfig(string file, out string runtimeConfigJson)
        {
            runtimeConfigJson = string.Empty;

            if (!File.Exists(file))
            {
                Console.WriteLine($"ERROR: Couldn't find config  file: {file}.");
                Console.WriteLine($"Please run: hawaii init <options> to create a new config file.");

                return false;
            }

            // Read existing config file content.
            //
            runtimeConfigJson = File.ReadAllText(file);
            return true;
        }
    }
}
