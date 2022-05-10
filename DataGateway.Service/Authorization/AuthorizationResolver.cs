using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Models.Authorization;
using Microsoft.AspNetCore.Http;
using Action = Azure.DataGateway.Config.Action;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// Authorization stages that require passing before a request is executed
    /// against a database.
    /// </summary>
    public class AuthorizationResolver : IAuthorizationResolver
    {
        private IRuntimeConfigProvider _runtimeConfigProvider;
        private Dictionary<string, EntityDS> _entityConfigMap;
        private const string CLIENT_ROLE_HEADER = "X-MS-API-ROLE";

        public AuthorizationResolver(IRuntimeConfigProvider runtimeConfigProvider)
        {
            _runtimeConfigProvider = runtimeConfigProvider;

            // Datastructure constructor will pull required properties from metadataprovider.
            _entityConfigMap = GetEntityConfigMap(_runtimeConfigProvider.GetRuntimeConfig());
        }

        /// <summary>
        /// Whether X-MS-API-Role Http Request Header is present in httpContext.Identity.Claims.Roles
        /// </summary>
        /// <param name="httpRequestData"></param>
        /// <returns>
        /// X-MS-API-Role
        ///     Header not present -> TRUE, request is anonymous
        ///     Header present, no value -> FALSE
        ///     Header present, invalid value -> FALSE
        ///     Header present, valid value -> TRUE
        /// </returns>
        /// <exception cref="NotImplementedException"></exception>
        public bool IsValidRoleContext(HttpContext httpContext)
        {
            // Anonymous requests must specifically set the Anonymous role.
            if (!httpContext.Request.Headers.ContainsKey(CLIENT_ROLE_HEADER))
            {
                return false;
            }

            // Multiple header fields with the same field-name(X-MS-API-ROLE) MAY be present in a message,
            // but are NOT supported.
            // Valid scenario per HTTP Spec: http://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2
            // Discussion: https://stackoverflow.com/a/3097052/18174950
            if (httpContext.Request.Headers[CLIENT_ROLE_HEADER].Count > 1)
            {
                return false;
            }

            if (httpContext.Request.Headers[CLIENT_ROLE_HEADER].ToString().Length == 0)
            {
                return false;
            }

            string clientRole = httpContext.Request.Headers[CLIENT_ROLE_HEADER].ToString();

            if (httpContext.User.IsInRole(clientRole))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Whether X-DG-Role Http Request Header value is present in DeveloperConfig:Entity
        /// This should fail if entity does not exist. For now: should be 403 Forbidden instead of 404
        /// to avoid leaking Schema data.
        /// </summary>
        /// <param name="roleName"></param>
        /// <param name="entityName"></param>
        /// <returns></returns>
        public bool IsRoleDefinedForEntity(string roleName, string entityName)
        {
            //At this point we don't know if entityName and roleName is valid/exists
            if (!_entityConfigMap.ContainsKey(entityName) || !_entityConfigMap[entityName].RoleToActionMap.ContainsKey(roleName))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether Entity.Role has action defined
        /// </summary>
        /// <param name="roleName"></param>
        /// <param name="entityName"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public bool IsActionAllowedForRole(string roleName, string entityName, string action)
        {
            // At this point we don't know if action is a valid action,
            // in sense that it exists for the given entity/role combination.

            Dictionary<string, ActionDS> actionToColumnMap = _entityConfigMap[entityName].RoleToActionMap[roleName].ActionToColumnMap;
            if (actionToColumnMap.ContainsKey("*") || actionToColumnMap.ContainsKey(action))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Compare columns in request body to columns in entity.Role.Action.AllowedColumns.
        /// This stage assumes that all provided columns are valid for entity (request validation occurs prior to this check).
        /// </summary>
        /// <param name="roleName"></param>
        /// <param name="entityName"></param>
        /// <param name="action"></param>
        /// <param name="columns"></param>
        /// <returns></returns>
        public bool AreColumnsAllowedForAction(string roleName, string entityName, string actionName, List<string> columns)
        {
            ActionDS actionToColumnMap;
            if (_entityConfigMap[entityName].RoleToActionMap[roleName].ActionToColumnMap.ContainsKey("*"))
            {
                actionToColumnMap = _entityConfigMap[entityName].RoleToActionMap[roleName].ActionToColumnMap["*"];
            }
            else
            {
                actionToColumnMap = _entityConfigMap[entityName].RoleToActionMap[roleName].ActionToColumnMap[actionName];
            }

            foreach (string column in columns)
            {
                if ((actionToColumnMap.excluded != null && (actionToColumnMap.excluded.Contains(column)|| actionToColumnMap.excluded.Contains("*"))) ||
                    !(actionToColumnMap.included != null && (actionToColumnMap.included.Contains("*") || actionToColumnMap.included.Contains(column))))
                {
                    // If column is present in excluded OR excluded='*'
                    // If column is absent from included and included!=*
                    // return false
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Processes policies.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="roleName"></param>
        /// <param name="httpContext"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public bool DidProcessDBPolicy(string action, string roleName, HttpContext httpContext)
        {
            throw new NotImplementedException();
        }

        #region Helpers
        /// <summary>
        /// Method to read in data from the config class into a Dictionary for quick lookup
        /// during runtime.
        /// </summary>
        /// <param name="runtimeConfig"></param>
        /// <returns></returns>
        private static Dictionary<string, EntityDS> GetEntityConfigMap(RuntimeConfig? runtimeConfig)
        {
            Dictionary<string, EntityDS> entityConfigMap = new();
            foreach ((string entityName,Entity entity) in runtimeConfig!.Entities)
            {
                EntityDS entityToRoleMap = new();

                foreach (PermissionSetting permission in entity.Permissions)
                {
                    string role = permission.Role;
                    RoleDS roleToAction = new();
                    object[] Actions = permission.Actions;
                    ActionDS actionToColumn;
                    foreach (object action in Actions)
                    {
                        JsonElement actionElement = JsonSerializer.SerializeToElement(action);
                        string actionName = string.Empty;
                        actionToColumn = new();
                        if (actionElement.ValueKind == JsonValueKind.String)
                        {
                            actionName = actionElement.ToString();
                            actionToColumn!.included!.Add("*");
                        }
                        else if (actionElement.ValueKind == JsonValueKind.Object)
                        {
                            JsonSerializerOptions options = new()
                            {
                                PropertyNameCaseInsensitive = true,
                                Converters = { new JsonStringEnumConverter() }
                            };

                            Action? actionObj = JsonSerializer.Deserialize<Action>(actionElement.ToString(), options);
                            actionName = actionObj!.Name;

                            if (actionObj!.Fields!.Include != null)
                            {
                                actionToColumn.included = AddFieldsToMap(actionObj.Fields.Include);
                            }

                            if (actionObj!.Fields!.Exclude != null)
                            {
                                actionToColumn.excluded = AddFieldsToMap(actionObj.Fields.Exclude);
                            }

                        }

                        roleToAction.ActionToColumnMap[actionName] = actionToColumn;
                    }

                    entityToRoleMap.RoleToActionMap[role] = roleToAction;
                }

                entityConfigMap[entityName] = entityToRoleMap;
            }

            return entityConfigMap;
        }

        /// <summary>
        /// Parses runtime config Included and Excluded columns into
        /// key/value store for use in the entityConfigMap.
        /// </summary>
        /// <param name="columns"></param>
        /// <returns></returns>
        private static HashSet<string> AddFieldsToMap(string[] columns)
        {
            HashSet<string> result = new();
            foreach (string column in columns)
            {
                result.Add(column);
            }

            return result;
        }
        #endregion
    }
}
