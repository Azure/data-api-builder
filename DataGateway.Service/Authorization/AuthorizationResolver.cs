using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models.Authorization;
using Action = Azure.DataGateway.Config.Action;
using Microsoft.AspNetCore.Http;

namespace Azure.DataGateway.Service.Authorization
{
    public class AuthorizationResolver : IAuthorizationResolver
    {
        private IRuntimeConfigProvider _runtimeConfigProvider;
        private Dictionary<string, EntityToRole> _entityConfigMap;

        public AuthorizationResolver(IRuntimeConfigProvider runtimeConfigProvider)
        {
            if (runtimeConfigProvider.GetType() != typeof(IRuntimeConfigProvider))
            {
                throw new DataGatewayException(
                    message: "Unable to instantiate the SQL query engine.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            _runtimeConfigProvider = (RuntimeConfigProvider)runtimeConfigProvider;

            // Datastructure constructor will pull required properties from metadataprovider.
            _entityConfigMap = GetEntityConfigMap(_runtimeConfigProvider.GetRuntimeConfig());
        }

        /// <summary>
        /// Whether X-DG-Role Http Request Header is present in httpContext.Identity.Claims.Roles
        /// </summary>
        /// <param name="httpRequestData"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public bool IsValidRoleContext(HttpRequest httpRequestData)
        {
            // TO-DO #1
            throw new NotImplementedException();
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
            if (!_entityConfigMap.ContainsKey(entityName) || !_entityConfigMap[entityName].roleToActionMap.ContainsKey(roleName))
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

            Dictionary<string, ActionToColumn> actionToColumnMap = _entityConfigMap[entityName].roleToActionMap[roleName].actionToColumnMap;
            if (actionToColumnMap.ContainsKey("*") || actionToColumnMap.ContainsKey(action))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Compare columns in request body to columns in entity.Role.Action.AllowedColumns
        /// </summary>
        /// <param name="roleName"></param>
        /// <param name="entityName"></param>
        /// <param name="action"></param>
        /// <param name="columns"></param>
        /// <returns></returns>
        public bool AreColumnsAllowedForAction(string roleName, string entityName, string action, List<string> columns)
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
                    object[] Actions = permission.Actions;
                    ActionToColumn actionToColumnMap;
                    foreach (object Action in Actions)
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
        #endregion
    }
}
