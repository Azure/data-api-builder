using System.Text.Json;
using System.Collections.Generic;
using System.IO;

namespace Azure.DataGateway.Service
{
    public class AuthZConfigDS
    {
        Dictionary<string, EntityToRole> _entityConfigMap;
        private bool ReadConfig()
        {
            string json;
            using (StreamReader sr = new("C:\\Users\\agarwalayush\\source\\repos\\JSONParser1\\JSONParser1\\RuntimeConfig.json"))
            {
                json = sr.ReadToEnd();
            }
            DraftDevConfig? devConfig = JsonSerializer.Deserialize<DraftDevConfig>(json);
            _entityConfigMap = GetEntityConfigMap(devConfig);

            bool ok = false;

            string entityName = "todo";
            string roleName = "public";
            string action = "update";
            List<string> columns = new() { "id", "title", "completed" };

            if (!IsRoleDefinedForEntity(roleName, entityName))
            {
                return false; //invalid entity/role
            }
            if (!IsActionAllowedForRole(roleName, entityName, action))
            {
                return false; //invalid action
            }
            if (!AreColumnsAllowedForAction(roleName, entityName, action, columns)) {
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
            // At this point we don't know if action is a valid action in sense that it exists for the given entity/role 
            // combination, and it is a valid action like CRUD and not an absurd action
            Dictionary<string, ActionToColumn> actionToColumnMap = _entityConfigMap[entityName].roleToActionMap[roleName].actionToColumnMap;
            if (actionToColumnMap.ContainsKey("*") || actionToColumnMap.ContainsKey(action))
            {
                return true;
            }

            return false;
        }

        private bool AreColumnsAllowedForAction(string roleName, string entityName, string action, List<string> columns)
        {
            // Asking point 3 for this , because if that is allowed, we can have custom included and excluded.
            // At this point, we are sure that action is a valid action. However if we had an action='*', this 
            // action would not be present in the entityConfigMap[entityName].roleToActionMap[roleName].actionToColumnMap
            // and hence the below ActionToColumn would be null. And hence we first need to make a check if '*' action is present
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

                if(!actionToColumnMap.excluded.ContainsKey(column) && !actionToColumnMap.included.ContainsKey(column))
                {
                    // If a column is absent from both excluded,included
                    // it can be valid/invalid.
                    // If the column turns out to be an invalid one
                    // an error would be thrown later.
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
        private static Dictionary<string, EntityToRole> GetEntityConfigMap(DraftDevConfig? devConfig)
        {

            Dictionary<string, EntityToRole> entityConfigMap = new ();

            foreach (KeyValuePair<string, DataGatewayEntity> Entity in devConfig.Entities)
            {
                string entityName = Entity.Key;
                DataGatewayEntity entity = Entity.Value;
                EntityToRole entityToRoleMap = new ();

                foreach (DataGatewayPermission permission in entity.Permissions)
                {
                    string role = permission.Role;
                    RoleToAction roleToActionMap = new ();
                    JsonElement jsonActions = (JsonElement)permission.Actions;
                    string action = string.Empty;
                    //jsonActionsKind can be a string,array of string and object
                    ActionToColumn actionToColumnMap;
                    if (jsonActions.ValueKind == JsonValueKind.String)
                    {
                        actionToColumnMap = new ActionToColumn();
                        actionToColumnMap.included.Add("*", true);
                        action = jsonActions.ToString();
                        roleToActionMap.actionToColumnMap[action] = actionToColumnMap;
                    }
                    else if (jsonActions.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement Operation in jsonActions.EnumerateArray())
                        {
                            actionToColumnMap = new ActionToColumn();
                            if (Operation.ValueKind == JsonValueKind.String)
                            {
                                action = Operation.ToString();
                                actionToColumnMap.included.Add("*", true);
                            }
                            else if (Operation.ValueKind == JsonValueKind.Object)
                            {
                                string ops = Operation.ToString();
                                ActionType? actionType = JsonSerializer.Deserialize<ActionType>(Operation.ToString());
                                action = actionType.Action;
                                if (actionType.Fields.Include != null)
                                {
                                    actionToColumnMap.included = AddFields(actionType.Fields.Include);
                                }

                                if (actionType.Fields.Exclude != null)
                                {
                                    actionToColumnMap.excluded = AddFields(actionType.Fields.Exclude);
                                }

                            }

                            roleToActionMap.actionToColumnMap[action] = actionToColumnMap;
                        }
                    }

                    entityToRoleMap.roleToActionMap[role] = roleToActionMap;
                }

                entityConfigMap[entityName] = entityToRoleMap;
            }

            return entityConfigMap;
        }
        private static Dictionary<string, bool> AddFields(List<string> columns)
        {
            Dictionary<string, bool> result = new ();
            foreach (string column in columns)
            {
                result[column] = true;
            }
            return result;
        }
    }
}
