using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Config;
using Action = Azure.DataGateway.Config.Action;

namespace Azure.DataGateway.Service
{
    public class AuthZConfigDS
    {
        Dictionary<string, EntityToRole>? _entityConfigMap;
        public bool ReadConfig()
        {
            string json;
            using (StreamReader sr = new("D:\\directory\\DataGateway.Service.Tests\\runtime-config-test.json"))
            {
                json = sr.ReadToEnd();
            }

            JsonSerializerOptions? options = new()
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(json, options);
            _entityConfigMap = GetEntityConfigMap(runtimeConfig);

            string entityName = "magazines";
            string roleName = "authenticated";
            string action = "read";
            List<string> columns = new() { "issue_number"};

            if (!IsRoleDefinedForEntity(roleName, entityName))
            {
                return false; //invalid entity/role
            }

            if (!IsActionAllowedForRole(roleName, entityName, action))
            {
                return false; //invalid action
            }

            if (!AreColumnsAllowedForAction(roleName, entityName, action, columns))
            {
                return false; //columns not allowed
            }

            return true;
        }

        private bool IsRoleDefinedForEntity(string roleName, string entityName)
        {
            //At this point we don't know if entityName and roleName is valid/exists
            if (!_entityConfigMap.ContainsKey(entityName) || !_entityConfigMap[entityName].roleToActionMap.ContainsKey(roleName))
            {
                return false;
            }

            return true;
        }

        private bool IsActionAllowedForRole(string roleName, string entityName, string action)
        {
            // At this point we don't know if action is a valid action,
            // in sense that it exists for the given entity/role combination.

            Dictionary<string, ActionToColumn> actionToColumnMap = _entityConfigMap[entityName].roleToActionMap[roleName].actionToColumnMap;
            if (actionToColumnMap.ContainsKey("*") || actionToColumnMap.ContainsKey(action))
            {
                return true;
            }

            return false;
        }

        private bool AreColumnsAllowedForAction(string roleName, string entityName, string action, List<string> columns)
        {
            ActionToColumn actionToColumnMap;
            if (_entityConfigMap[entityName].roleToActionMap[roleName].actionToColumnMap.ContainsKey("*"))
            {
                actionToColumnMap = _entityConfigMap[entityName].roleToActionMap[roleName].actionToColumnMap["*"];
            }
            else
            {
                actionToColumnMap = _entityConfigMap[entityName].roleToActionMap[roleName].actionToColumnMap[action];
            }

            foreach (string column in columns)
            {

                if (!actionToColumnMap!.excluded!.ContainsKey(column) && !actionToColumnMap!.included!.ContainsKey(column))
                {
                    // If a column is absent from both excluded,included
                    // it can be valid/invalid.
                    // If the column turns out to be an invalid one
                    // an error would be thrown during request validation.
                    continue;
                }

                if (actionToColumnMap.excluded.ContainsKey(column) ||
                    !(actionToColumnMap.included != null && (actionToColumnMap.included.ContainsKey("*") || actionToColumnMap.included.ContainsKey(column))))
                {
                    // If column is present in excluded OR
                    // If column is absent from included and included!=*
                    // return false
                    return false;
                }

            }

            return true;
        }

        // Method to read in data from the config class into a Dictionary for quick lookup
        // during runtime.
        private static Dictionary<string, EntityToRole> GetEntityConfigMap(RuntimeConfig? runtimeConfig)
        {

            Dictionary<string, EntityToRole> entityConfigMap = new();

            foreach (KeyValuePair<string, Entity> entityPair in runtimeConfig!.Entities)
            {
                string entityName = entityPair.Key;
                Entity entity = entityPair.Value;
                EntityToRole entityToRoleMap = new();

                foreach (PermissionSetting permission in entity.Permissions)
                {
                    string role = permission.Role;
                    RoleToAction roleToActionMap = new();
                    Object[] Actions = permission.Actions;
                    ActionToColumn actionToColumnMap;
                    foreach (Object Action in Actions)
                    {
                        JsonElement action = (JsonElement)Action;
                        string actionName = "";
                        actionToColumnMap = new ActionToColumn();
                        if (action.ValueKind == JsonValueKind.String)
                        {
                            actionName = action.ToString();
                            actionToColumnMap!.included!.Add("*", true);
                        }
                        else if (action.ValueKind == JsonValueKind.Object)
                        {
                            JsonSerializerOptions options = new()
                            {
                                PropertyNameCaseInsensitive = true,
                                Converters = { new JsonStringEnumConverter() }
                            };

                            Action? actionObj = JsonSerializer.Deserialize<Action>(action.ToString(), options);
                            actionName = actionObj!.Name;

                            if (actionObj!.Fields!.Include != null)
                            {
                                actionToColumnMap.included = AddFieldsToMap(actionObj.Fields.Include);
                            }

                            if (actionObj!.Fields!.Exclude != null)
                            {
                                actionToColumnMap.excluded = AddFieldsToMap(actionObj.Fields.Exclude);
                            }

                        }

                        roleToActionMap.actionToColumnMap[actionName] = actionToColumnMap;
                    }

                    entityToRoleMap.roleToActionMap[role] = roleToActionMap;
                }

                entityConfigMap[entityName] = entityToRoleMap;
            }

            return entityConfigMap;
        }
        private static Dictionary<string, bool> AddFieldsToMap(string[] columns)
        {
            Dictionary<string, bool> result = new();
            foreach (string column in columns)
            {
                result[column] = true;
            }

            return result;
        }
    }
}
