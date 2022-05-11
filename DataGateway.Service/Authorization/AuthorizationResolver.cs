using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Models.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Action = Azure.DataGateway.Config.Action;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// Authorization stages that require passing before a request is executed
    /// against a database.
    /// </summary>
    public class AuthorizationResolver : IAuthorizationResolver
    {
        private Dictionary<string, EntityDS> _entityConfigMap;
        private const string CLIENT_ROLE_HEADER = "X-MS-API-ROLE";

        public AuthorizationResolver(IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath)
        {
            // Datastructure constructor will pull required properties from metadataprovider.
            _entityConfigMap = GetEntityConfigMap(runtimeConfigPath.CurrentValue.ConfigValue!);
        }

        /// <summary>
        /// Whether client role header defined role is present in httpContext.Identity.Claims.Roles
        /// and if the header is present, whether the authenticated user is a member of the role defined
        /// in the header.
        /// </summary>
        /// <param name="httpContext">Contains request headers and metadata of the authenticated user.</param>
        /// <returns>
        /// Client Role HEader
        ///     Header not present -> FALSE, anonymous request must still provided required header.
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

            // Multiple header fields with the same field-name MAY be present in a message,
            // but are NOT supported, specifically for the client role header.
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

        /// <inheritdoc />
        public bool AreRoleAndActionDefinedForEntity(string entityName, string roleName, string action)
        {
            if (_entityConfigMap.TryGetValue(entityName, out EntityDS? value))
            {
                if (value.RoleToActionMap.ContainsKey(roleName))
                {
                    Dictionary<string, ActionDS> actionToColumnMap = _entityConfigMap[entityName].RoleToActionMap[roleName].ActionToColumnMap;
                    if (actionToColumnMap.ContainsKey("*") || actionToColumnMap.ContainsKey(action))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        public bool AreColumnsAllowedForAction(string entityName, string roleName, string actionName, List<string> columns)
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
                if (actionToColumnMap.excluded.Contains(column) || actionToColumnMap.excluded.Contains("*") ||
                    !(actionToColumnMap.included.Contains("*") || actionToColumnMap.included.Contains(column)))
                {
                    // If column is present in excluded OR excluded='*'
                    // If column is absent from included and included!=*
                    // return false
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public bool DidProcessDBPolicy(string entityName, string roleName, string action, HttpContext httpContext)
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
            foreach ((string entityName, Entity entity) in runtimeConfig!.Entities)
            {
                EntityDS entityToRoleMap = new();

                foreach (PermissionSetting permission in entity.Permissions)
                {
                    string role = permission.Role;
                    RoleDS roleToAction = new();
                    JsonElement[] Actions = permission.Actions;
                    ActionDS actionToColumn;
                    foreach (JsonElement actionElement in Actions)
                    {
                        string actionName = string.Empty;
                        actionToColumn = new();
                        if (actionElement.ValueKind == JsonValueKind.String)
                        {
                            actionName = actionElement.ToString();
                            actionToColumn.included.Add("*");
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
                                AddFieldsToSet(actionObj.Fields.Include, actionToColumn.included);
                            }

                            if (actionObj!.Fields!.Exclude != null)
                            {
                                AddFieldsToSet(actionObj.Fields.Exclude, actionToColumn.excluded);
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
        private static void AddFieldsToSet(string[] columns, HashSet<string> Fields)
        {
            foreach (string column in columns)
            {
                Fields.Add(column);
            }
        }
        #endregion
    }
}
