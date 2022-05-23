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
        //
        // Summary:
        //      This mehod will generate the initial config with databaseType and connection-string.
        public static bool GenerateConfig(string fileName, string? resolverConfigFile, string database_type, string connection_string)
        {
            DatabaseType dbType;
            try {
                dbType = Enum.Parse<DatabaseType>(database_type.ToLower());
            } catch(Exception e) {
                Console.WriteLine($"Unsupported databaseType: {database_type}. Supported values: mssql,cosomos,mysql,postgresql");
                return false;
            }

            DataSource dataSource = new DataSource(dbType);
            dataSource.ConnectionString = connection_string;

            string file = $"{fileName}.json";

            string schema = RuntimeConfig.SCHEMA;

            CosmosDbOptions? cosmosDbOptions = null;
            MsSqlOptions? msSqlOptions = null;
            MySqlOptions? mySqlOptions = null;
            PostgreSqlOptions? postgreSqlOptions = null;

            
            switch(dbType) {
                case DatabaseType.cosmos: 
                    if(resolverConfigFile==null) {
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

                default: Console.WriteLine($"DatabaseType: ${database_type} not supported.Please provide a valid database-type.");
                        return false;
            }

            RuntimeConfig runtimeConfig = new RuntimeConfig(schema, dataSource, CosmosDb: cosmosDbOptions, MsSql: msSqlOptions,
                                                PostgreSql: postgreSqlOptions, MySql: mySqlOptions,
                                                GetDefaultGlobalSettings(dbType), new Dictionary<string, Entity>());

            string JSONresult = JsonSerializer.Serialize(runtimeConfig, GetSerializationOptions());

            if (File.Exists(file)) {
                File.Delete(file);
            }

            File.WriteAllText(file, JSONresult);
            return true;
        }


        //
        // Summary:
        //      This mehod will add a new Entity with the given restand graphql endpoints, source, and permissions.
        //      It also support fields that needs to encluded or excluded for a given role and action.
        public static bool AddEntitiesToConfig(string fileName, string entity,
                                            object source, string permissions,
                                            object? rest, object? graphQL,
                                            string? fieldsToInclude, string? fieldsToExclude)
        {
            string file = $"{fileName}.json";

            if (!File.Exists(file))
            {
                Console.WriteLine($"ERROR: Couldn't find config  file: {file}.");
                Console.WriteLine($"Please do hawaii init <options> to create a new config file.");
                return false;
            }

            string[] permission_array = permissions.Split(":");
            if(permission_array.Length is not 2) {
                Console.WriteLine("Please add permission in the following format. --permission \"<<role>>:<<actions>>\"");
                return false;
            }

            string role = permission_array[0];
            string actions = permission_array[1];
            PermissionSetting[] permissionSettings = new PermissionSetting[]{CreatePermissions(role, actions, fieldsToInclude, fieldsToExclude)};
            Entity entity_details = new Entity(source, GetRestDetails(rest), GetGraphQLDetails(graphQL), permissionSettings, Relationships: null, Mappings: null);

            string jsonString = File.ReadAllText(file);
            JsonSerializerOptions options = GetSerializationOptions();

            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString, options);
            
            if(runtimeConfig.Entities.ContainsKey(entity)) {
                Console.WriteLine($"WARNING: Entity-{entity} is already present. No new changes are added to Config.");
                return false;
            }

            runtimeConfig.Entities.Add(entity, entity_details);
            string JSONresult = JsonSerializer.Serialize(runtimeConfig, options);
            File.WriteAllText(file, JSONresult);
            return true;
        }


        //
        // Summary:
        //      This mehod will update an excisting Entity with the given restand graphql endpoints, source, and permissions.
        //      It also support adding a new relationship as well as updating an excisting one.
        public static bool UpdateEntity(string fileName, string entity,
                                            object? source, string? permissions,
                                            object? rest, object? graphQL,
                                            string? fieldsToInclude, string? fieldsToExclude,
                                            string? relationship, string? targetEntity,
                                            string? cardinality, string? mappingFields) {
            
            string file = $"{fileName}.json";
            string jsonString = File.ReadAllText(file);
            JsonSerializerOptions options = GetSerializationOptions();

            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString, options);
            if(!runtimeConfig.Entities.ContainsKey(entity)) {
                Console.WriteLine($"Entity:{entity} is not present. No new changes are added to Config.");
                return false;
            }
            
            Entity updatedEntity = runtimeConfig.Entities[entity];
            if(source is not null) {
                updatedEntity = new Entity(source, updatedEntity.Rest, updatedEntity.GraphQL, updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }

            if(rest is not null) {
                updatedEntity = new Entity(updatedEntity.Source, GetRestDetails(rest), updatedEntity.GraphQL, updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }

            if(graphQL is not null) {
                updatedEntity = new Entity(updatedEntity.Source, updatedEntity.Rest, GetGraphQLDetails(graphQL), updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }

            if(permissions is not null) {
                string[] permission_array = permissions.Split(":");
                if(permission_array.Length is not 2) {
                    Console.WriteLine("Please add permission in the following format. --permission \"<<role>>:<<actions>>\"");
                    return false;
                }
                
                string new_role = permission_array[0];
                string new_action = permission_array[1];
                PermissionSetting? dict = Array.Find(updatedEntity.Permissions, item => item.Role == new_role);
                PermissionSetting[] updatedPermissions;
                List<PermissionSetting> permissionSettingsList = new List<PermissionSetting>();
                if(dict==null) {
                    updatedPermissions = AddNewPermissions(updatedEntity.Permissions, new_role, new_action, fieldsToInclude, fieldsToExclude);
                } else {
                    string[] new_action_elements = new_action.Split(",");
                    if(new_action_elements.Length>1) {
                        Console.WriteLine($"ERROR: we currently support updating only one action operation.");
                        return false;
                    }

                    string new_action_element = new_action;
                    foreach(PermissionSetting permission in updatedEntity.Permissions) {    //Loop through current permissions for an entity.
                        if(permission.Role==new_role) { // Updating an excisting permission
                            string operation = GetCRUDOperation((JsonElement)permission.Actions[0]);
                            if(permission.Actions.Length==1 && "*".Equals(operation)) { // if the role had only one action and that is "*"
                                if(operation == new_action_element) { // if the new and old action is "*"
                                    permissionSettingsList.Add(CreatePermissions(permission.Role,"*",fieldsToInclude, fieldsToExclude));
                                } else { // if the new action is other than "*"
                                    List<ConfigAction> action_list = new List<ConfigAction>();
                                    foreach(object crud in Enum.GetValues(typeof(CRUD))) {      //looping through all the CRUD operations and updating the one which is asked
                                        string op = crud.ToString();
                                        if(op.Equals(new_action_element)) { //if the current crud operation is equal to the asked crud operation
                                            action_list.Add(GetAction(op, fieldsToInclude, fieldsToExclude));
                                        } else {    // else we just create a new node and add it with excisting properties
                                            string[]? currentFieldsToInclude = null;
                                            string[]? currentFieldsToExclude = null;
                                            if(!JsonValueKind.String.Equals(((JsonElement)permission.Actions[0]).ValueKind)) {
                                                Field fields_dict = ((ConfigAction)permission.Actions[0]).Fields;
                                                action_list.Add(new ConfigAction(op, Policy: null, Fields: new Field(fields_dict.Include, fields_dict.Exclude)));
                                            }

                                            action_list.Add(new ConfigAction(op, Policy: null, Fields: null));
                                        }
                                    }

                                    permissionSettingsList.Add(new PermissionSetting(permission.Role, action_list.ToArray()));
                                }
                            } else {
                                if("*".Equals(new_action_element)) {
                                    permissionSettingsList.Add(new PermissionSetting(permission.Role, CreateActions(new_action_element, fieldsToInclude, fieldsToExclude)));
                                } else {
                                    List<object> action_list = new ();
                                    foreach(JsonElement action_element in permission.Actions) {
                                        operation = GetCRUDOperation(action_element);
                                        if(new_action_element.Equals(operation)) {
                                            action_list.Add(GetAction(operation, fieldsToInclude, fieldsToExclude));
                                        } else {
                                            action_list.Add(action_element);
                                        }
                                    }

                                   permissionSettingsList.Add(new PermissionSetting(permission.Role, action_list.ToArray()));
                                }
                            }
                        } else { //Adding a new permission
                            permissionSettingsList.Add(permission);
                        }
                    }

                    updatedPermissions = permissionSettingsList.ToArray();
                }

                updatedEntity = new Entity(updatedEntity.Source, updatedEntity.Rest, updatedEntity.GraphQL, updatedPermissions, updatedEntity.Relationships, updatedEntity.Mappings);
            } else {
                if(fieldsToInclude is not null || fieldsToExclude is not null) {
                    Console.WriteLine($"please provide the role and action name to apply this update to.");
                    return true;
                }
            }
            if(relationship is not null) {
                //if it's an existing relation
                if(updatedEntity.Relationships is not null && updatedEntity.Relationships.ContainsKey(relationship)) {
                    Relationship currentRelationship = updatedEntity.Relationships[relationship];
                    Dictionary<string, Relationship> relationship_mapping = new Dictionary<string, Relationship>();
                    Relationship updatedRelationship = currentRelationship;
                    if(cardinality is not null) {
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
                    if(targetEntity is not null) {
                        if(!runtimeConfig.Entities.ContainsKey(targetEntity)) {
                            Console.WriteLine($"Entity:{targetEntity} is not present. No new changes are added to Config.");
                            return false;
                        }

                        updatedRelationship = new Relationship(updatedRelationship.Cardinality, targetEntity, updatedRelationship.SourceFields, updatedRelationship.TargetFields, updatedRelationship.LinkingObject, updatedRelationship.LinkingSourceFields, updatedRelationship.LinkingTargetFields);
                    }
                    if(mappingFields is not null) {
                        string[]? sourceAndTargetFields = null;
                        string[]? sourceFields = null;
                        string[]? targetFields = null;
                        try
                        {
                            sourceAndTargetFields = mappingFields.Split(":");
                            if(sourceAndTargetFields.Length is not 2) {
                                throw new Exception();
                            }

                            sourceFields = sourceAndTargetFields[0].Split(",");
                            targetFields = sourceAndTargetFields[1].Split(",");
                        }
                        catch (System.Exception)
                        {
                            Console.WriteLine($"ERROR: Please use correct format for --mappings.fields, It should be \"<<source.fields>>:<<target.fields>>\".");
                            return false;
                        }
                        
                        updatedRelationship = new Relationship(updatedRelationship.Cardinality, updatedRelationship.TargetEntity, sourceFields, targetFields, updatedRelationship.LinkingObject, updatedRelationship.LinkingSourceFields, updatedRelationship.LinkingTargetFields);
                        updatedEntity.Relationships[relationship] = updatedRelationship;
                    }
                } else {    // if it's a new relationship
                    if(cardinality is not null && targetEntity is not null && mappingFields is not null) {
                        Dictionary<string, Relationship> relationship_mapping = updatedEntity.Relationships==null ? new Dictionary<string, Relationship>(): updatedEntity.Relationships;
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

                        if(!runtimeConfig.Entities.ContainsKey(targetEntity)) {
                            Console.WriteLine($"Entity:{targetEntity} is not present. No new changes are added to Config.");
                            return false;
                        }

                        string[]? sourceAndTargetFields = null;
                        string[]? sourceFields = null;
                        string[]? targetFields = null;
                        try
                        {
                            sourceAndTargetFields = mappingFields.Split(":");
                            if(sourceAndTargetFields.Length is not 2) {
                                throw new Exception();
                            }
                            sourceFields = sourceAndTargetFields[0].Split(",");
                            targetFields = sourceAndTargetFields[1].Split(",");
                        }
                        catch (System.Exception)
                        {
                            Console.WriteLine($"ERROR: Please use correct format for --mappings.fields, It should be \"<<source.fields>>:<<target.fields>>\".");
                            return false;
                        }
                        
                        relationship_mapping.Add(relationship, new Relationship(cardinalityType, targetEntity, sourceFields, targetFields, LinkingObject: null, LinkingSourceFields: null, LinkingTargetFields: null));
                        updatedEntity = new Entity(updatedEntity.Source, updatedEntity.Rest, updatedEntity.GraphQL, updatedEntity.Permissions, relationship_mapping, updatedEntity.Mappings);
                    } else {
                        Console.WriteLine($"ERROR: For adding a new relationship following options are mandatory: --relationship, --cardinality, --target.entity, --mappings.field.");
                        return false;
                    }
                }
            }
            
            runtimeConfig.Entities[entity] = updatedEntity;
            string JSONresult = JsonSerializer.Serialize(runtimeConfig, options);
            File.WriteAllText(file, JSONresult);
            return true;
        }

    }
}
