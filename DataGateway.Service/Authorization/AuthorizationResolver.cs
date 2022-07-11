using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Http;
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
        private const string WILDCARD = "*";
        public const string CLAIM_PREFIX = "@claims.";
        public const string FIELD_PREFIX = "@item.";
        public const string CLIENT_ROLE_HEADER = "X-MS-API-ROLE";
        private const string SHORT_CLAIM_TYPE_NAME = "http://schemas.xmlsoap.org/ws/2005/05/identity/claimproperties/ShortTypeName";
        public Dictionary<string, EntityMetadata> EntityPermissionsMap { get; private set; } = new();

        public AuthorizationResolver(
            RuntimeConfigProvider runtimeConfigProvider,
            ISqlMetadataProvider sqlMetadataProvider
            )
        {
            _metadataProvider = sqlMetadataProvider;
            if (runtimeConfigProvider.TryGetRuntimeConfiguration(out RuntimeConfig? runtimeConfig))
            {
                // Datastructure constructor will pull required properties from metadataprovider.
                SetEntityPermissionMap(runtimeConfig);
            }
            else
            {
                runtimeConfigProvider.RuntimeConfigLoaded +=
                    (object? sender, RuntimeConfig config) => SetEntityPermissionMap(config);
            }
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
            // If the roleName is null, this indicates absence of the
            // X-MS-API-ROLE header in the http request. In such a case, request is assumed
            // to have anonymous role.
            if (roleName is null)
            {
                roleName = AuthorizationType.Anonymous.ToString().ToLower();
            }

            if (EntityPermissionsMap.TryGetValue(entityName, out EntityMetadata? valueOfEntityToRole))
            {
                if (valueOfEntityToRole.RoleToActionMap.TryGetValue(roleName, out RoleMetadata? valueOfRoleToAction))
                {
                    if (valueOfRoleToAction!.ActionToColumnMap.ContainsKey(WILDCARD) ||
                        valueOfRoleToAction!.ActionToColumnMap.ContainsKey(action))
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
            RoleMetadata roleInEntity = EntityPermissionsMap[entityName].RoleToActionMap[roleName];

            try
            {
                actionToColumnMap = roleInEntity.ActionToColumnMap[actionName];
            }
            catch (KeyNotFoundException)
            {
                actionToColumnMap = roleInEntity.ActionToColumnMap[WILDCARD];
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
                    if (actionToColumnMap.Excluded.Contains(backingColumn!) || actionToColumnMap.Excluded.Contains(WILDCARD) ||
                    !(actionToColumnMap.Included.Contains(WILDCARD) || actionToColumnMap.Included.Contains(backingColumn!)))
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
        public string TryProcessDBPolicy(string entityName, string roleName, string action, HttpContext httpContext)
        {
            string dBpolicyWithClaimTypes = GetDBPolicyForRequest(entityName, roleName, action);
            return string.IsNullOrWhiteSpace(dBpolicyWithClaimTypes) ? string.Empty :
                   ProcessClaimsForPolicy(dBpolicyWithClaimTypes, httpContext);
        }

        /// <summary>
        /// Helper function to fetch the database policy associated with the current request based on the entity under
        /// action, the role defined in the the request and the action to be executed.
        /// </summary>
        /// <param name="entityName">Entity from request.</param>
        /// <param name="roleName">Role defined in client role header.</param>
        /// <param name="action">Action type: create, read, update, delete.</param>
        /// <returns></returns>
        private string GetDBPolicyForRequest(string entityName, string roleName, string action)
        {
            // Fetch the database policy by using the sequence of following steps:
            // _entityPermissionMap[entityName] finds the entityMetaData for the current entityName
            // entityMetaData.RoleToActionMap[roleName] finds the roleMetaData for the current roleName
            // roleMetaData.ActionToColumnMap[action] finds the actionMetaData for the current action
            // actionMetaData.databasePolicy finds the required database policy
            RoleMetadata roleMetadata = EntityPermissionsMap[entityName].RoleToActionMap[roleName];
            roleMetadata.ActionToColumnMap.TryGetValue(action, out ActionMetadata? actionMetadata);

            // If action exists in map (explicitly specified in config), use its policy
            // action should only be absent in roleMetadata if WILDCARD is in the map instead of specific actions,
            // as authorization happens before policy parsing (would have already returned forbidden)
            string? dbPolicy;
            if (actionMetadata is not null)
            {
                dbPolicy = actionMetadata.DatabasePolicy;

            } // else check if wildcard exists in action map, if so use its policy, else null
            else
            {
                roleMetadata.ActionToColumnMap.TryGetValue(WILDCARD, out ActionMetadata? wildcardMetadata);
                dbPolicy = wildcardMetadata is not null ? wildcardMetadata.DatabasePolicy : null;
            }

            return dbPolicy is not null ? dbPolicy : string.Empty;
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
                        if (actionElement.ValueKind is JsonValueKind.String)
                        {
                            actionName = actionElement.ToString();
                            actionToColumn.Included.UnionWith(allTableColumns);
                            actionToColumn.Allowed.UnionWith(allTableColumns);
                        }
                        else
                        {
                            // If not a string, the actionObj is expected to be an object that can be deserialised into Action object.
                            // We will put validation checks later to make sure this is the case.
                            Action? actionObj = JsonSerializer.Deserialize<Action>(actionElement.ToString(), RuntimeConfig.GetDeserializationOptions());
                            if (actionObj is not null)
                            {
                                actionName = actionObj.Name;
                                if (actionObj.Fields!.Include is not null)
                                {
                                    // When a wildcard (*) is defined for Included columns, all of the table's
                                    // columns must be resolved and placed in the actionToColumn Key/Value store.
                                    // This is especially relevant for find requests, where actual column names must be
                                    // resolved when no columns were included in a request.
                                    if (actionObj.Fields.Include.Count == 1 && actionObj.Fields.Include.Contains(WILDCARD))
                                    {
                                        actionToColumn.Included.UnionWith(ResolveTableDefinitionColumns(entityName));
                                    }
                                    else
                                    {
                                        actionToColumn.Included = actionObj.Fields.Include!;
                                    }
                                }

                                if (actionObj.Fields!.Exclude is not null)
                                {
                                    // When a wildcard (*) is defined for Excluded columns, all of the table's
                                    // columns must be resolved and placed in the actionToColumn Key/Value store.
                                    if (actionObj.Fields.Exclude.Count == 1 && actionObj.Fields.Exclude.Contains(WILDCARD))
                                    {
                                        actionToColumn.Excluded.UnionWith(ResolveTableDefinitionColumns(entityName));
                                    }
                                    else
                                    {
                                        actionToColumn.Excluded = actionObj.Fields.Exclude!;
                                    }
                                }

                                if (actionObj.Policy is not null && actionObj.Policy.Database is not null)
                                {
                                    actionToColumn.DatabasePolicy = actionObj.Policy.Database;
                                }

                                // Calculate the set of allowed backing column names.
                                actionToColumn.Allowed.UnionWith(actionToColumn.Included.Except(actionToColumn.Excluded));
                            }
                        }

                        // Try to add the actionName to the map if not present.
                        // Builds up mapping: i.e. ActionType.CREATE permitted in {Role1, Role2, ..., RoleN}
                        if (!string.IsNullOrWhiteSpace(actionName) && !entityToRoleMap.ActionToRolesMap.TryAdd(actionName, new List<string>(new string[] { role })))
                        {
                            entityToRoleMap.ActionToRolesMap[actionName].Add(role);
                        }

                        roleToAction.ActionToColumnMap[actionName] = actionToColumn;
                    }

                    entityToRoleMap.RoleToActionMap[role] = roleToAction;
                }

                EntityPermissionsMap[entityName] = entityToRoleMap;
            }
        }

        /// <inheritdoc />
        public IEnumerable<string> GetAllowedColumns(string entityName, string roleName, string action)
        {
            ActionMetadata actionMetadata = EntityPermissionsMap[entityName].RoleToActionMap[roleName].ActionToColumnMap[action];
            IEnumerable<string> allowedDBColumns = actionMetadata.Allowed;
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
        /// Helper method to process the given policy obtained from config, and convert it into an injectable format in
        /// the HttpContext object by substituting @claim.xyz claims with their values.
        /// </summary>
        /// <param name="policy">The policy to be processed.</param>
        /// <param name="context">HttpContext object used to extract all the claims available in the request.</param>
        /// <returns>Processed policy string that can be injected into the HttpContext object.</returns>
        private static string ProcessClaimsForPolicy(string policy, HttpContext context)
        {
            Dictionary<string, Claim> claimsInRequestContext = GetAllUserClaims(context);
            policy = GetPolicyWithClaimValues(policy, claimsInRequestContext);
            return policy;
        }

        /// <summary>
        /// Helper method to extract all claims available in the HttpContext object and
        /// add them all in the claimsInRequestContext dictionary which is used later for quick lookup
        /// of different claimTypes and their corresponding claimValues.
        /// </summary>
        /// <param name="context">HttpContext object used to extract all the claims available in the request.</param>
        /// <param name="claimsInRequestContext">Dictionary to hold all the claims available in the request.</param>
        private static Dictionary<string, Claim> GetAllUserClaims(HttpContext context)
        {
            Dictionary<string, Claim> claimsInRequestContext = new();
            ClaimsIdentity? identity = (ClaimsIdentity?)context.User.Identity;

            if (identity is null)
            {
                return claimsInRequestContext;
            }

            foreach (Claim claim in identity.Claims)
            {
                /*
                 * An example claim would be of format:
                 * claim.Type: "user_email"
                 * claim.Value: "authz@microsoft.com"
                 * claim.ValueType: "string"
                 */
                // If a claim has a short type name, use it (i.e. 'roles' instead of 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role')
                string type = claim.Properties.TryGetValue(SHORT_CLAIM_TYPE_NAME, out string? shortName) ? shortName : claim.Type;
                // Don't add roles to the claims dictionary and don't throw an exception in the case of multiple role claims,
                // since a user can have multiple roles assigned and role resolution happens beforehand
                if (claim.Type is not ClaimTypes.Role && !claimsInRequestContext.TryAdd(type, claim))
                {
                    // If there are duplicate claims present in the request, return an exception.
                    throw new DataGatewayException(
                        message: $"Duplicate claims are not allowed within a request.",
                        statusCode: System.Net.HttpStatusCode.Forbidden,
                        subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed
                        );
                }
            }

            return claimsInRequestContext;
        }

        /// <summary>
        /// Helper method to substitute all the claimTypes(denoted with @claims.claimType) in
        /// the policy string with their corresponding claimValues.
        /// </summary>
        /// <param name="policy">The policy to be processed.</param>
        /// <param name="claimsInRequestContext">Dictionary holding all the claims available in the request.</param>
        /// <returns>Processed policy with claim values substituted for claim types.</returns>
        /// <exception cref="DataGatewayException"></exception>
        private static string GetPolicyWithClaimValues(string policy, Dictionary<string, Claim> claimsInRequestContext)
        {
            // Regex used to extract all claimTypes in policy. It finds all the substrings which are
            // of the form @claims.*** where *** contains characters from a-zA-Z0-9._ .
            string claimCharsRgx = @"@claims\.[a-zA-Z0-9_\.]*";

            // Find all the claimTypes from the policy
            string processedPolicy = Regex.Replace(policy, claimCharsRgx,
                (claimTypeMatch) => GetClaimValueFromClaim(claimTypeMatch, claimsInRequestContext));

            //Remove occurences of @item. directives
            processedPolicy = processedPolicy.Replace(AuthorizationResolver.FIELD_PREFIX, "");
            return processedPolicy;
        }

        /// <summary>
        /// Helper function used to retrieve the claim value for the given claim type from the user's claims.
        /// </summary>
        /// <param name="claimTypeMatch">The claimType present in policy with a prefix of @claims..</param>
        /// <param name="claimsInRequestContext">Dictionary populated with all the user claims.</param>
        /// <returns>The claim value for the given claimTypeMatch.</returns>
        /// <exception cref="DataGatewayException"> Throws exception when the user does not possess the given claim.</exception>
        private static string GetClaimValueFromClaim(Match claimTypeMatch, Dictionary<string, Claim> claimsInRequestContext)
        {
            string claimType = claimTypeMatch.Value.ToString().Substring(AuthorizationResolver.CLAIM_PREFIX.Length);
            if (claimsInRequestContext.TryGetValue(claimType, out Claim? claim))
            {
                return GetClaimValueByDataType(claim);
            }
            else
            {
                // User lacks a claim which is required to perform the action.
                throw new DataGatewayException(
                    message: "User does not possess all the claims required to perform this action.",
                    statusCode: System.Net.HttpStatusCode.Forbidden,
                    subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed
                    );
            }
        }

        /// <summary>
        /// Helper function to return the claim value enclosed within a parenthesis alongwith the required additonal
        /// quotes if required. This makes sure we adhere to JSON specifications where strings are enclosed in
        /// single quotes while int,bool,double etc are not.
        /// </summary>
        /// <param name="claim">The claim whose value is to be returned.</param>
        /// <returns>Processed claim value based on its data type.</returns>
        /// <exception cref="DataGatewayException">Exception thrown when the claim's datatype is not supported.</exception>
        private static string GetClaimValueByDataType(Claim claim)
        {
            /* An example claim would be of format:
             * claim.Type: "user_email"
             * claim.Value: "authz@microsoft.com"
             * claim.ValueType: "string"
             */

            switch (claim.ValueType)
            {
                case ClaimValueTypes.String:
                    return $"('{claim.Value}')";
                case ClaimValueTypes.Boolean:
                case ClaimValueTypes.Integer32:
                case ClaimValueTypes.Integer64:
                case ClaimValueTypes.Double:
                    return $"({claim.Value})";
                default:
                    // One of the claims in the request had unsupported data type.
                    throw new DataGatewayException(
                        message: "One or more claims have data types which are not supported yet.",
                        statusCode: System.Net.HttpStatusCode.Forbidden,
                        subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed
                    );
            }
        }

        /// <summary>
        /// Get list of roles defined for entity within runtime configuration.. This is applicable for GraphQL when creating authorization
        /// directive on Object type.
        /// </summary>
        /// <param name="entityName">Name of entity.</param>
        /// <returns>Collection of role names.</returns>
        public IEnumerable<string> GetRolesForEntity(string entityName)
        {
            return EntityPermissionsMap[entityName].RoleToActionMap.Keys;
        }

        /// <summary>
        /// Returns a list of roles which define permissions for the provided action.
        /// i.e. list of roles which allow the action "read" on entityName.
        /// </summary>
        /// <param name="entityName">Entity to lookup permissions</param>
        /// <param name="actionName">Action to lookup applicable roles</param>
        /// <returns>Collection of roles.</returns>
        public IEnumerable<string> GetRolesForAction(string entityName, string actionName)
        {
            if (EntityPermissionsMap[entityName].ActionToRolesMap.TryGetValue(actionName, out List<string>? roleList) && roleList is not null)
            {
                return roleList;
            }

            return new List<string>();
        }

        /// <summary>
        /// Returns the collection of roles which can perform {actionName} the provided field.
        /// Applicable to GraphQL field directive @authorize on ObjectType fields.
        /// </summary>
        /// <param name="entityName">EntityName whose actionMetadata will be searched.</param>
        /// <param name="actionName">ActionName to lookup field permissions</param>
        /// <param name="field">Specific field to get collection of roles</param>
        /// <returns>Collection of role names allowed to perform actionName on Entity's field.</returns>
        public IEnumerable<string> GetRolesForField(string entityName, string actionName, string field)
        {
            return EntityPermissionsMap[entityName].FieldToRolesMap[actionName][field];
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
