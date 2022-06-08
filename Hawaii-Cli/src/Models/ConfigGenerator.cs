using System.Text.Json;
using Azure.DataGateway.Config;
using static Hawaii.Cli.Models.Utils;
using ConfigAction = Azure.DataGateway.Config.Action;

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
        public static bool GenerateConfig(string fileName, string? resolverConfigFile, string database_type, string connection_string, string? hostMode)
        {
            DatabaseType dbType;

            try
            {
                dbType = Enum.Parse<DatabaseType>(database_type.ToLower());
            }
            catch (Exception)
            {
                Console.WriteLine($"Unsupported databaseType: {database_type}. Supported values: mssql,cosomos,mysql,postgresql");
                return false;
            }

            DataSource? dataSource = new(dbType)
            {
                ConnectionString = connection_string
            };

            string? file = $"{fileName}.json";

            string? schema = RuntimeConfig.SCHEMA;

            CosmosDbOptions? cosmosDbOptions = null;
            MsSqlOptions? msSqlOptions = null;
            MySqlOptions? mySqlOptions = null;
            PostgreSqlOptions? postgreSqlOptions = null;

            switch (dbType)
            {
                case DatabaseType.cosmos:
                    if (resolverConfigFile is null)
                    {
                        Console.WriteLine($"resolver-config-file: ${resolverConfigFile} not supported.Please provide a valid Resolver Config File for CosmosDB.");
                        return false;
                    }

                    cosmosDbOptions = new CosmosDbOptions(database_type, resolverConfigFile);
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
                    Console.WriteLine($"DatabaseType: ${database_type} not supported.Please provide a valid database-type.");
                    return false;
            }

            RuntimeConfig runtimeConfig;
            try
            {
                runtimeConfig = new RuntimeConfig(schema, dataSource, CosmosDb: cosmosDbOptions, MsSql: msSqlOptions,
                                                    PostgreSql: postgreSqlOptions, MySql: mySqlOptions,
                                                    GetDefaultGlobalSettings(dbType, hostMode), new Dictionary<string, Entity>());
            }
            catch (NotSupportedException e)
            {
                Console.WriteLine($"{e}");
                return false;
            }

            string? JSONresult = JsonSerializer.Serialize(runtimeConfig, GetSerializationOptions());

            if (File.Exists(file))
            {
                File.Delete(file);
            }

            File.WriteAllText(file, JSONresult);
            return true;
        }

        /// <summary>
        /// This method will add a new Entity with the given REST and GraphQL endpoints, source, and permissions.
        /// It also supports fields that needs to be included or excluded for a given role and action.
        /// </summary>
        public static bool AddEntitiesToConfig(string fileName, string entity,
                                            object source, string permissions,
                                            string? rest, string? graphQL,
                                            string? fieldsToInclude, string? fieldsToExclude)
        {
            string? file = $"{fileName}.json";

            if (!File.Exists(file))
            {
                Console.WriteLine($"ERROR: Couldn't find config  file: {file}.");
                Console.WriteLine($"Please do hawaii init <options> to create a new config file.");
                return false;
            }

            string[]? permission_array = permissions.Split(":");
            if (permission_array.Length is not 2)
            {
                Console.WriteLine("Please add permission in the following format. --permission \"<<role>>:<<actions>>\"");
                return false;
            }

            string? role = permission_array[0];
            string? actions = permission_array[1];
            PermissionSetting[]? permissionSettings = new PermissionSetting[] { CreatePermissions(role, actions, fieldsToInclude, fieldsToExclude) };
            Entity? entity_details = new(source, GetRestDetails(rest), GetGraphQLDetails(graphQL), permissionSettings, Relationships: null, Mappings: null);

            string? jsonString = File.ReadAllText(file);
            JsonSerializerOptions? options = GetSerializationOptions();

            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString, options);

            if (runtimeConfig!.Entities.ContainsKey(entity))
            {
                Console.WriteLine($"WARNING: Entity-{entity} is already present. No new changes are added to Config.");
                return false;
            }

            runtimeConfig.Entities.Add(entity, entity_details);
            string? JSONresult = JsonSerializer.Serialize(runtimeConfig, options);
            File.WriteAllText(file, JSONresult);
            return true;
        }

        /// <summary>
        /// This method will update an existing Entity with the given REST and GraphQL endpoints, source, and permissions.
        /// It also supports adding a new relationship as well as updating an existing one.
        /// </summary>
        public static bool UpdateEntity(string fileName, string entity,
                                            object? source, string? permissions,
                                            string? rest, string? graphQL,
                                            string? fieldsToInclude, string? fieldsToExclude,
                                            string? relationship, string? cardinality,
                                            string? targetEntity, string? linkingObject,
                                            string? linkingSourceFields, string? linkingTargetFields,
                                            string? mappingFields)
        {

            string? file = $"{fileName}.json";
            string? jsonString = File.ReadAllText(file);
            JsonSerializerOptions? options = GetSerializationOptions();

            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString, options);
            if (!runtimeConfig!.Entities.ContainsKey(entity))
            {
                Console.WriteLine($"Entity:{entity} is not present. No new changes are added to Config.");
                return false;
            }

            Entity? updatedEntity = runtimeConfig.Entities[entity];
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
                string[]? permission_array = permissions.Split(":");
                if (permission_array.Length is not 2)
                {
                    Console.WriteLine("Please add permission in the following format. --permission \"<<role>>:<<actions>>\"");
                    return false;
                }

                string? new_role = permission_array[0];
                string? new_action = permission_array[1];
                PermissionSetting? dict = Array.Find(updatedEntity.Permissions, item => item.Role == new_role);
                PermissionSetting[] updatedPermissions;
                List<PermissionSetting>? permissionSettingsList = new();
                if (dict is null)
                {
                    updatedPermissions = AddNewPermissions(updatedEntity.Permissions, new_role, new_action, fieldsToInclude, fieldsToExclude);
                }
                else
                {
                    string[]? new_action_elements = new_action.Split(",");
                    if (new_action_elements.Length > 1)
                    {
                        Console.WriteLine($"ERROR: we currently support updating only one action operation.");
                        return false;
                    }

                    string? new_action_element = new_action;
                    foreach (PermissionSetting? permission in updatedEntity.Permissions)
                    {    //Loop through current permissions for an entity.
                        if (permission.Role == new_role)
                        { // Updating an existing permission
                            string? operation = GetCRUDOperation((JsonElement)permission.Actions[0]);
                            if (permission.Actions.Length == 1 && "*".Equals(operation))
                            { // if the role had only one action and that is "*"
                                if (operation == new_action_element)
                                { // if the new and old action is "*"
                                    permissionSettingsList.Add(CreatePermissions(permission.Role, "*", fieldsToInclude, fieldsToExclude));
                                }
                                else
                                {
                                    // if the new action is other than "*"
                                    List<ConfigAction>? action_list = new();

                                    // Looping through all the CRUD operations and updating the one which is asked
                                    foreach (string op in Enum.GetNames(typeof(CRUD)))
                                    {
                                        // If the current crud operation is equal to the asked crud operation
                                        if (op.Equals(new_action_element, StringComparison.OrdinalIgnoreCase))
                                        {
                                            action_list.Add(GetAction(op, fieldsToInclude, fieldsToExclude));
                                        }
                                        // Else we just create a new node and add it with existing properties
                                        else
                                        {
                                            if (!JsonValueKind.String.Equals(((JsonElement)permission.Actions[0]).ValueKind))
                                            {
                                                Field? fields_dict = ((ConfigAction)permission.Actions[0]).Fields;
                                                action_list.Add(new ConfigAction(op, Policy: null, Fields: new Field(fields_dict.Include, fields_dict.Exclude)));
                                            }

                                            action_list.Add(new ConfigAction(op, Policy: null, Fields: null));
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
                    Relationship? currentRelationship = updatedEntity.Relationships[relationship];
                    Dictionary<string, Relationship>? relationship_mapping = new();
                    Relationship? updatedRelationship = currentRelationship;
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
                        Dictionary<string, Relationship>? relationship_mapping = updatedEntity.Relationships is null ? new Dictionary<string, Relationship>() : updatedEntity.Relationships;
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

                    Relationship? updatedRelationship = updatedEntity.Relationships[relationship];
                    updatedRelationship = new Relationship(updatedRelationship.Cardinality, updatedRelationship.TargetEntity,
                                                            sourceFields, targetFields, updatedRelationship.LinkingObject,
                                                            updatedRelationship.LinkingSourceFields, updatedRelationship.LinkingTargetFields);

                    updatedEntity.Relationships[relationship] = updatedRelationship;
                }

                if (linkingObject is not null && linkingSourceFields is not null && linkingTargetFields is not null)
                {
                    string[]? linkingSourceFieldsArray = linkingSourceFields.Split(",");
                    string[]? linkingTargetFieldsArray = linkingTargetFields.Split(",");

                    Relationship? updatedRelationship = updatedEntity.Relationships[relationship];
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

            runtimeConfig.Entities[entity] = updatedEntity;
            string? JSONresult = JsonSerializer.Serialize(runtimeConfig, options);
            File.WriteAllText(file, JSONresult);
            return true;
        }

    }
}
