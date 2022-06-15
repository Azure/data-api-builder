using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Models.Authorization;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Action = Azure.DataGateway.Config.Action;

namespace Azure.DataGateway.Service.Authorization
{
    /// <summary>
    /// Authorization stages that require passing before a request is executed
    /// against a database.
    /// </summary>
    public class AuthorizationResolver : IAuthorizationResolver
    {
        private ISqlMetadataProvider _metadataProvider;
        private Dictionary<string, EntityMetadata> _entityPermissionMap = new();
        private const string WILDCARD = "*";
        private static readonly HashSet<string> _validActions = new() { ActionType.CREATE, ActionType.READ, ActionType.UPDATE, ActionType.DELETE };

        public const string CLIENT_ROLE_HEADER = "X-MS-API-ROLE";

        public AuthorizationResolver(
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath,
            ISqlMetadataProvider sqlMetadataProvider
            )
        {
            // Datastructure constructor will pull required properties from metadataprovider.
            _metadataProvider = sqlMetadataProvider;
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
            StringValues clientRoleHeader = httpContext.Request.Headers[CLIENT_ROLE_HEADER];

            // The clientRoleHeader must be present on requests.
            // Consequentially, anonymous requests must specifically set
            // the clientRoleHeader value to Anonymous.
            if (clientRoleHeader.Count == 0)
            {
                return false;
            }

            // Multiple header fields with the same field-name MAY be present in a message,
            // but are NOT supported, specifically for the client role header.
            // Valid scenario per HTTP Spec: http://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2
            // Discussion: https://stackoverflow.com/a/3097052/18174950
            if (clientRoleHeader.Count > 1)
            {
                return false;
            }

            string clientRoleHeaderValue = clientRoleHeader.ToString();

            // The clientRoleHeader must have a value.
            if (clientRoleHeaderValue.Length == 0)
            {
                return false;
            }

            return httpContext.User.IsInRole(clientRoleHeaderValue);
        }

        /// <inheritdoc />
        public bool AreRoleAndActionDefinedForEntity(string entityName, string roleName, string action)
        {
            if (_entityPermissionMap.TryGetValue(entityName, out EntityMetadata? valueOfEntityToRole))
            {
                if (valueOfEntityToRole.RoleToActionMap.TryGetValue(roleName, out RoleMetadata valueOfRoleToAction))
                {
                    if (valueOfRoleToAction.ActionToColumnMap.ContainsKey(WILDCARD) ||
                        valueOfRoleToAction.ActionToColumnMap.ContainsKey(action))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        public bool AreColumnsAllowedForAction(string entityName, string roleName, string actionName, IEnumerable<string> columns)
        {
            // Columns.Count() will never be zero because this method is called after a check ensures Count() > 0
            Assert.IsFalse(columns.Count() == 0, message: "columns.Count() should be greater than 0.");

            ActionMetadata actionToColumnMap;
            RoleMetadata roleInEntity = _entityPermissionMap[entityName].RoleToActionMap[roleName];

            try
            {
                actionToColumnMap = roleInEntity.ActionToColumnMap[WILDCARD];
            }
            catch (KeyNotFoundException)
            {
                actionToColumnMap = roleInEntity.ActionToColumnMap[actionName];
            }

            // Each column present in the request is an "exposedColumn".
            // Authorization permissions reference "backingColumns"
            // Resolve backingColumn name to check authorization.
            // Failure indicates that request contain invalid exposedColumn for entity.
            foreach (string exposedColumn in columns)
            {
                if (_metadataProvider.TryGetBackingColumn(entityName, field: exposedColumn, out string? backingColumn))
                {
                    // backingColumn will not be null when TryGetBackingColumn() is true. 
                    if (actionToColumnMap.excluded.Contains(backingColumn!) || actionToColumnMap.excluded.Contains(WILDCARD) ||
                    !(actionToColumnMap.included.Contains(WILDCARD) || actionToColumnMap.included.Contains(backingColumn!)))
                    {
                        // If column is present in excluded OR excluded='*'
                        // If column is absent from included and included!=*
                        // return false
                        return false;
                    }
                }
                else
                {
                    // This check will not be needed once exposedName mapping validation is added.
                    throw new DataGatewayException(
                        message: "Invalid field name provided.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataGatewayException.SubStatusCodes.ExposedColumnNameMappingError
                        );
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
                EntityMetadata entityToRoleMap = new();

                foreach (PermissionSetting permission in entity.Permissions)
                {
                    string role = permission.Role;
                    RoleMetadata roleToAction = new();
                    object[] Actions = permission.Actions;
                    foreach (JsonElement actionElement in Actions)
                    {
                        string actionName = string.Empty;
                        ActionMetadata actionToColumn = new();
                        IEnumerable<string> allTableColumns = ResolveTableDefinitionColumns(entityName);
                        if (actionElement.ValueKind == JsonValueKind.String)
                        {
                            actionName = actionElement.ToString();
                            actionToColumn.included.UnionWith(allTableColumns);
                            actionToColumn.allowed.UnionWith(allTableColumns);
                        }
                        else if (actionElement.ValueKind == JsonValueKind.Object)
                        {
                            Action? actionObj = JsonSerializer.Deserialize<Action>(actionElement.ToString(), RuntimeConfig.GetDeserializationOptions());
                            if (actionObj is not null)
                            {
                                actionName = actionObj.Name;

                                //Assert the assumption that the actionName is valid.
                                Assert.IsTrue(IsValidActionName(actionName));

                                if (actionObj.Fields!.Include is not null)
                                {
                                    // When a wildcard (*) is defined for Included columns, all of the table's
                                    // columns must be resolved and placed in the actionToColumn Key/Value store.
                                    // This is especially relevant for find requests, where actual column names must be
                                    // resolved when no columns were included in a request.
                                    if (actionObj.Fields.Include.Length == 1 && actionObj.Fields.Include[0] == WILDCARD)
                                    {
                                        actionToColumn.included.UnionWith(ResolveTableDefinitionColumns(entityName));
                                    }
                                    else
                                    {
                                        actionToColumn.included = new(actionObj.Fields.Include);
                                    }
                                }

                                if (actionObj.Fields!.Exclude is not null)
                                {
                                    // When a wildcard (*) is defined for Excluded columns, all of the table's
                                    // columns must be resolved and placed in the actionToColumn Key/Value store.
                                    if (actionObj.Fields.Exclude.Length == 1 && actionObj.Fields.Exclude[0] == WILDCARD)
                                    {
                                        actionToColumn.excluded.UnionWith(ResolveTableDefinitionColumns(entityName));
                                    }
                                    else
                                    {
                                        actionToColumn.excluded = new(actionObj.Fields.Exclude);
                                    }
                                }

                                // Calculate the set of allowed backing column names.
                                actionToColumn.allowed.UnionWith(actionToColumn.included.Except(actionToColumn.excluded));
                            }
                        }

                        roleToAction.ActionToColumnMap[actionName] = actionToColumn;
                    }

                    entityToRoleMap.RoleToActionMap[role] = roleToAction;
                }

                _entityPermissionMap[entityName] = entityToRoleMap;
            }
        }

        /// <inheritdoc />
        public IEnumerable<string> GetAllowedColumns(string entityName, string roleName, string action)
        {
            ActionMetadata actionMetadata = _entityPermissionMap[entityName].RoleToActionMap[roleName].ActionToColumnMap[action];
            IEnumerable<string> allowedDBColumns = actionMetadata.allowed;
            List<string> allowedExposedColumns = new();

            foreach (string dbColumn in allowedDBColumns)
            {
                if (_metadataProvider.TryGetExposedColumnName(entityName, backingFieldName: dbColumn, out string? exposedName))
                {
                    allowedExposedColumns.Append(exposedName);
                }
            }

            return allowedExposedColumns;
        }

        /// <summary>
        /// Returns whether the actionName is a valid
        /// - Create, Read, Update, Delete (CRUD) operation
        /// - Wildcard (*)
        /// </summary>
        /// <param name="actionName"></param>
        /// <returns></returns>
        private static bool IsValidActionName(string actionName)
        {
            if (actionName.Equals(WILDCARD) || _validActions.Contains(actionName))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get list of roles defined for entity within runtime configuration.. This is applicable for GraphQL when creating authorization
        /// directive on Object type.
        /// </summary>
        /// <param name="entityName">Name of entity.</param>
        /// <returns>Collection of role names.</returns>
        public IEnumerable<string> GetRolesForEntity(string entityName)
        {
            return _entityPermissionMap[entityName].RoleToActionMap.Keys;
        }

        /// <summary>
        /// Returns the collection of roles which can READ the provided field.
        /// Applicable to GraphQL field directive @authorize on ObjectType fields.
        /// </summary>
        /// <param name="entityName"></param>
        /// <param name="actionName"></param>
        /// <param name="field"></param>
        /// <returns></returns>
        public IEnumerable<string> GetRolesForField(string entityName, string actionName, string field)
        {
            return _entityPermissionMap[entityName].FieldToRolesMap[actionName][field];
        }

        /// <summary>
        /// For a given entityName, retrieve the column names on the associated table
        /// from the metadataProvider.
        /// </summary>
        /// <param name="entityName">Used to lookup table definition of specific entity</param>
        /// <returns>Collection of columns in table definition.</returns>
        private IEnumerable<string> ResolveTableDefinitionColumns(string entityName)
        {
            return _metadataProvider.GetTableDefinition(entityName).Columns.Keys;
        }
        #endregion
    }
}
