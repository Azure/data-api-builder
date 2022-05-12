using System;
using System.Collections.Generic;
using System.Text.Json;
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
        private Dictionary<string, EntityDS> _entityPermissionMap = new();
        private const string CLIENT_ROLE_HEADER = "X-MS-API-ROLE";

        public AuthorizationResolver(IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath)
        {
            // Datastructure constructor will pull required properties from metadataprovider.
            SetEntityPermissionMap(runtimeConfigPath.CurrentValue.ConfigValue!);
        }

        /// <summary>
        /// Whether client role header defined role is present in httpContext.Identity.Claims.Roles
        /// and if the header is present, whether the authenticated user is a member of the role defined
        /// in the header.
        /// </summary>
        /// <param name="httpContext">Contains request headers and metadata of the authenticated user.</param>
        /// <returns>
        /// Client Role Header
        ///     Header not present -> FALSE, anonymous request must still provide required header.
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

            return httpContext.User.IsInRole(clientRole);
        }

        /// <inheritdoc />
        public bool AreRoleAndActionDefinedForEntity(string entityName, string roleName, string action)
        {
            if (_entityPermissionMap.TryGetValue(entityName, out EntityDS? valueOfEntityToRole))
            {
                if (valueOfEntityToRole.RoleToActionMap.TryGetValue(roleName,out RoleDS? valueOfRoleToAction))
                {
                    if (valueOfRoleToAction.ActionToColumnMap.ContainsKey("*") ||
                        valueOfRoleToAction.ActionToColumnMap.ContainsKey(action))
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
            if (_entityPermissionMap[entityName].RoleToActionMap[roleName].ActionToColumnMap.ContainsKey("*"))
            {
                actionToColumnMap = _entityPermissionMap[entityName].RoleToActionMap[roleName].ActionToColumnMap["*"];
            }
            else
            {
                actionToColumnMap = _entityPermissionMap[entityName].RoleToActionMap[roleName].ActionToColumnMap[actionName];
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
        public void SetEntityPermissionMap(RuntimeConfig? runtimeConfig)
        {
            foreach ((string entityName, Entity entity) in runtimeConfig!.Entities)
            {
                EntityDS entityToRoleMap = new();

                foreach (PermissionSetting permission in entity.Permissions)
                {
                    string role = permission.Role;
                    RoleDS roleToAction = new();
                    JsonElement[] Actions = permission.Actions;
                    foreach (JsonElement actionElement in Actions)
                    {
                        string actionName = string.Empty;
                        ActionDS actionToColumn = new();
                        if (actionElement.ValueKind == JsonValueKind.String)
                        {
                            actionName = actionElement.ToString();
                            actionToColumn.included.Add("*");
                        }
                        else if (actionElement.ValueKind == JsonValueKind.Object)
                        {
                            Action? actionObj = JsonSerializer.Deserialize<Action>(actionElement.ToString(), RuntimeConfig.GetDeserializationOptions());
                            actionName = actionObj!.Name;

                            if (actionObj!.Fields!.Include is not null)
                            {
                                actionToColumn.included = new(actionObj.Fields.Include);
                            }

                            if (actionObj!.Fields!.Exclude is not null)
                            {
                                actionToColumn.excluded = new(actionObj.Fields.Exclude);
                            }

                        }

                        roleToAction.ActionToColumnMap[actionName] = actionToColumn;
                    }

                    entityToRoleMap.RoleToActionMap[role] = roleToAction;
                }

                _entityPermissionMap[entityName] = entityToRoleMap;
            }
        }
        #endregion
    }
}
