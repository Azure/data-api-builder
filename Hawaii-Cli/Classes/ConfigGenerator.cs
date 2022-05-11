using System.Text.Json;
using Azure.DataGateway.Config;
using static Hawaii.Cli.Classes.Utils;

namespace Hawaii.Cli.Classes
{
    public class ConfigGenerator
    {
        public static bool GenerateConfig(string fileName, string database_type, string connection_string)
        {

            DatabaseType dbType = Enum.Parse<DatabaseType>(database_type);
            DataSource dataSource = new DataSource(dbType, connection_string);

            string file = fileName + ".json";

            string schema = Directory.GetCurrentDirectory().Replace("\\", "/") + "/" + file;

            RuntimeConfig runtimeConfig = new RuntimeConfig(schema, dataSource, null, null, null, null, null, new Dictionary<string, Entity>());

            string JSONresult = JsonSerializer.Serialize(runtimeConfig, GetSerializationOptions());

            if (File.Exists(file)) {
                File.Delete(file);
            }
            File.WriteAllText(file, JSONresult);
            return true;
        }

        public static bool AddEntitiesToConfig(string fileName, string entity,
                                             object source, string permissions,
                                             object? rest, object? graphQL, string? fieldsToInclude, string? fieldsToExclude)
        {
            string file = fileName + ".json";

            if (!File.Exists(file))
            {
                Console.WriteLine($"ERROR: Couldn't find config  file: {file}.");
                Console.WriteLine($"Please do hawaii init <options> to create a new config file.");
                return false;
            }
            string[] permission_array = permissions.Split(":");
            string role = permission_array[0];
            string actions = permission_array[1];
            PermissionSetting[] permissionSettings = new PermissionSetting[]{CreatePermissions(role, actions, fieldsToInclude, fieldsToExclude)};
            Entity entity_details = new Entity(source, GetRestDetails(rest), GetGraphQLDetails(graphQL), permissionSettings, null, null);

            string jsonString = File.ReadAllText(file);
            var options = GetSerializationOptions();

            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString, options);
            
            if(runtimeConfig.Entities.ContainsKey(entity)) {
                Console.WriteLine($"WARNING: Entity-{entity} is already present. No new changes are added to Config.");
                return false;
            }

            runtimeConfig.Entities.Add(entity, entity_details);
            Console.WriteLine(runtimeConfig.ToString());
            string JSONresult = JsonSerializer.Serialize(runtimeConfig, options);
            File.WriteAllText(file, JSONresult);
            return true;
        }

        public static bool UpdateEntity(string fileName, string entity,
                                             object? source, string? permissions,
                                             object? rest, object? graphQL, string? fieldsToInclude, string? fieldsToExclude) {
            
            string file = fileName + ".json";
            string jsonString = File.ReadAllText(file);
            var options = GetSerializationOptions();

            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString, options);
            if(!runtimeConfig.Entities.ContainsKey(entity)) {
                Console.WriteLine($"Entity:{entity} is not present. No new changes are added to Config.");
                return false;
            }
            
            Entity updatedEntity = runtimeConfig.Entities[entity];
            if(source!=null) {
                updatedEntity = new Entity(source, updatedEntity.Rest, updatedEntity.GraphQL, updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }
            if(rest!=null) {
                updatedEntity = new Entity(updatedEntity.Source, GetRestDetails(rest), updatedEntity.GraphQL, updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }
            if(graphQL!=null) {
                updatedEntity = new Entity(updatedEntity.Source, updatedEntity.Rest, GetGraphQLDetails(graphQL), updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }
            if(permissions!=null) {
                string[] permission_array = permissions.Split(":");
                string new_role = permission_array[0];
                string new_action = permission_array[1]; //TODO: make sure action is a single item here
                var dict = Array.Find(updatedEntity.Permissions, item => item.Role == new_role);
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
                        if(permission.Role==new_role) {
                            string operation = GetCRUDOperation((JsonElement)permission.Actions[0]);
                            if(permission.Actions.Length==1 && "*".Equals(operation)) {
                                if(operation == new_action_element) {
                                    permissionSettingsList.Add(CreatePermissions(permission.Role,"*",fieldsToInclude, fieldsToExclude));
                                } else {
                                    List<Action> action_list = new List<Action>();
                                    foreach(object crud in Enum.GetValues(typeof(CRUD))) {
                                        string op = crud.ToString();
                                        if(op.Equals(new_action_element)) {
                                            action_list.Add(Action.GetAction(op, fieldsToInclude, fieldsToExclude));
                                        } else {
                                            string? currentFieldsToInclude = null;
                                            string? currentFieldsToExclude = null;
                                            if(!JsonValueKind.String.Equals(((JsonElement)permission.Actions[0]).ValueKind)) {
                                                Dictionary<string, string[]> fields_dict = Action.ToObject((JsonElement)permission.Actions[0]).fields;
                                                currentFieldsToInclude = fields_dict.ContainsKey("include")? string.Join(",",fields_dict.GetValueOrDefault("include")): null;
                                                currentFieldsToExclude = fields_dict.ContainsKey("exclude")? string.Join(",",fields_dict.GetValueOrDefault("exclude")): null;
                                            }
                                            action_list.Add(Action.GetAction(op, currentFieldsToInclude, currentFieldsToExclude));
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
                                            action_list.Add(Action.GetAction(operation, fieldsToInclude, fieldsToExclude));
                                        } else {
                                            // if(JsonValueKind.String.Equals(action_element.ValueKind)) {
                                            //     return Action.GetAction(element.ToString(), null);
                                            // }
                                            action_list.Add(action_element);
                                        }
                                    }
                                   permissionSettingsList.Add(new PermissionSetting(permission.Role, action_list.ToArray()));
                                }
                            }
                        } else {
                            permissionSettingsList.Add(permission);
                        }
                    }
                    updatedPermissions = permissionSettingsList.ToArray();
                }
                updatedEntity = new Entity(updatedEntity.Source, updatedEntity.Rest, updatedEntity.GraphQL, updatedPermissions, updatedEntity.Relationships, updatedEntity.Mappings);
            } else {
                if(fieldsToInclude!=null || fieldsToExclude!=null) {
                    Console.WriteLine($"please provide the role and action name to apply this update to.");
                    return true;
                }
            }
            
            runtimeConfig.Entities[entity] = updatedEntity;
            string JSONresult = JsonSerializer.Serialize(runtimeConfig, options);
            File.WriteAllText(file, JSONresult);
            return true;
        }

    }
}