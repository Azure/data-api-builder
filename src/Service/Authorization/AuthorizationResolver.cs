using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Action = Azure.DataApiBuilder.Config.Action;

namespace Azure.DataApiBuilder.Service.Authorization
{
    /// <summary>
    /// Authorization stages that require passing before a request is executed
    /// against a database.
    /// </summary>
    public class AuthorizationResolver : IAuthorizationResolver
    {
        private ISqlMetadataProvider _metadataProvider;
        public const string WILDCARD = "*";
        public const string CLAIM_PREFIX = "@claims.";
        public const string FIELD_PREFIX = "@item.";
        public const string CLIENT_ROLE_HEADER = "X-MS-API-ROLE";
        public const string ROLE_ANONYMOUS = "anonymous";
        public const string ROLE_AUTHENTICATED = "authenticated";
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

            if (clientRoleHeader.Count != 1)
            {
                // When count = 0, the clientRoleHeader is absent on requests.
                // Consequentially, anonymous requests must specifically set
                // the clientRoleHeader value to Anonymous.

                // When count > 1, multiple header fields with the same field-name
                // are present in a message, but are NOT supported, specifically for the client role header.
                // Valid scenario per HTTP Spec: http://www.w3.org/Protocols/rfc2616/rfc2616-sec4.html#sec4.2
                // Discussion: https://stackoverflow.com/a/3097052/18174950
                return false;
            }

            string clientRoleHeaderValue = clientRoleHeader.ToString();

            // The clientRoleHeader must have a value.
            if (clientRoleHeaderValue.Length == 0)
            {
                return false;
            }

            // IsInRole looks at all the claims present in the request
            // Reference: https://github.com/microsoft/referencesource/blob/master/mscorlib/system/security/claims/ClaimsPrincipal.cs
            return httpContext.User.IsInRole(clientRoleHeaderValue);
        }

        /// <inheritdoc />
        public bool AreRoleAndActionDefinedForEntity(string entityName, string roleName, Operation action)
        {
            if (EntityPermissionsMap.TryGetValue(entityName, out EntityMetadata? valueOfEntityToRole))
            {
                if (valueOfEntityToRole.RoleToActionMap.TryGetValue(roleName, out RoleMetadata? valueOfRoleToAction))
                {
                    if (valueOfRoleToAction!.ActionToColumnMap.ContainsKey(action))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        public bool AreColumnsAllowedForAction(string entityName, string roleName, Operation action, IEnumerable<string> columns)
        {
            // Columns.Count() will never be zero because this method is called after a check ensures Count() > 0
            Assert.IsFalse(columns.Count() == 0, message: "columns.Count() should be greater than 0.");

            ActionMetadata actionToColumnMap = EntityPermissionsMap[entityName].RoleToActionMap[roleName].ActionToColumnMap[action];

            // Each column present in the request is an "exposedColumn".
            // Authorization permissions reference "backingColumns"
            // Resolve backingColumn name to check authorization.
            // Failure indicates that request contain invalid exposedColumn for entity.
            foreach (string exposedColumn in columns)
            {
                if (_metadataProvider.TryGetBackingColumn(entityName, field: exposedColumn, out string? backingColumn))
                {
                    // backingColumn will not be null when TryGetBackingColumn() is true.
                    if (actionToColumnMap.Excluded.Contains(backingColumn!) ||
                        !actionToColumnMap.Included.Contains(backingColumn!))
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
                    throw new DataApiBuilderException(
                        message: "Invalid field name provided.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ExposedColumnNameMappingError
                        );
                }
            }

            return true;
        }

        /// <inheritdoc />
        public string TryProcessDBPolicy(string entityName, string roleName, Operation action, HttpContext httpContext)
        {
            string dBpolicyWithClaimTypes = GetDBPolicyForRequest(entityName, roleName, action);
            return string.IsNullOrWhiteSpace(dBpolicyWithClaimTypes) ? string.Empty :
                   ProcessClaimsForPolicy(dBpolicyWithClaimTypes, httpContext);
        }

        /// <summary>
        /// Helper function to fetch the database policy associated with the current request based on the entity under
        /// action, the role defined in the the request and the action to be executed.
        /// When no database policy is found, no database query predicates need to be added.
        /// 1) _entityPermissionMap[entityName] finds the entityMetaData for the current entityName
        /// 2) entityMetaData.RoleToActionMap[roleName] finds the roleMetaData for the current roleName
        /// 3) roleMetaData.ActionToColumnMap[action] finds the actionMetaData for the current action
        /// 4) actionMetaData.databasePolicy finds the required database policy
        /// </summary>
        /// <param name="entityName">Entity from request.</param>
        /// <param name="roleName">Role defined in client role header.</param>
        /// <param name="action">Action type: create, read, update, delete.</param>
        /// <returns>Policy string if a policy exists in config.</returns>
        private string GetDBPolicyForRequest(string entityName, string roleName, Operation action)
        {
            if (!EntityPermissionsMap[entityName].RoleToActionMap.TryGetValue(roleName, out RoleMetadata? roleMetadata))
            {
                return string.Empty;
            }

            if (!roleMetadata.ActionToColumnMap.TryGetValue(action, out ActionMetadata? actionMetadata))
            {
                return string.Empty;
            }

            // Get the database policy for the specified action.
            string? dbPolicy = actionMetadata.DatabasePolicy;

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

                // Store the allowedColumns for anonymous role.
                // In case the authenticated role is not defined on the entity,
                // this will help in copying over permissions from anonymous role to authenticated role.
                HashSet<string> allowedColumnsForAnonymousRole = new();
                foreach (PermissionSetting permission in entity.Permissions)
                {
                    string role = permission.Role;
                    RoleMetadata roleToAction = new();
                    object[] Actions = permission.Actions;
                    foreach (JsonElement actionElement in Actions)
                    {
                        Operation action = Operation.None;
                        ActionMetadata actionToColumn = new();

                        // Use a hashset to store all the backing field names
                        // that are accessible to the user.
                        HashSet<string> allowedColumns = new();
                        IEnumerable<string> allTableColumns = ResolveTableDefinitionColumns(entityName);

                        // Implicitly, all table columns are 'allowed' when an actiontype is a string.
                        // Since no granular field permissions exist for this action within the current role.
                        if (actionElement.ValueKind is JsonValueKind.String)
                        {
                            string actionName = actionElement.ToString();
                            action = AuthorizationResolver.WILDCARD.Equals(actionName) ? Operation.All : Enum.Parse<Operation>(actionName, ignoreCase: true);
                            actionToColumn.Included.UnionWith(allTableColumns);
                            allowedColumns.UnionWith(allTableColumns);
                        }
                        else
                        {
                            // If not a string, the actionObj is expected to be an object that can be deserialised into Action object.
                            // We will put validation checks later to make sure this is the case.
                            if (RuntimeConfig.TryGetDeserializedConfig(actionElement.ToString(), out Action? actionObj)
                                && actionObj is not null)
                            {
                                action = actionObj.Name;
                                if (actionObj.Fields is null)
                                {
                                    actionToColumn.Included.UnionWith(ResolveTableDefinitionColumns(entityName));
                                }
                                else
                                {
                                    if (actionObj.Fields.Include is not null)
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
                                            actionToColumn.Included = actionObj.Fields.Include;
                                        }
                                    }

                                    if (actionObj.Fields.Exclude is not null)
                                    {
                                        // When a wildcard (*) is defined for Excluded columns, all of the table's
                                        // columns must be resolved and placed in the actionToColumn Key/Value store.
                                        if (actionObj.Fields.Exclude.Count == 1 && actionObj.Fields.Exclude.Contains(WILDCARD))
                                        {
                                            actionToColumn.Excluded.UnionWith(ResolveTableDefinitionColumns(entityName));
                                        }
                                        else
                                        {
                                            actionToColumn.Excluded = actionObj.Fields.Exclude;
                                        }
                                    }
                                }

                                if (actionObj.Policy is not null && actionObj.Policy.Database is not null)
                                {
                                    actionToColumn.DatabasePolicy = actionObj.Policy.Database;
                                }

                                // Calculate the set of allowed backing column names.
                                allowedColumns.UnionWith(actionToColumn.Included.Except(actionToColumn.Excluded));
                            }
                        }

                        // Populate allowed exposed columns for each entity/role/action combination during startup,
                        // so that it doesn't need to be evaluated per request.
                        PopulateAllowedExposedColumns(actionToColumn.AllowedExposedColumns, entityName, allowedColumns);

                        IEnumerable<Operation> actions = GetAllActions(action);
                        foreach (Operation actionOp in actions)
                        {
                            // Try to add the actionOp to the map if not present.
                            // Builds up mapping: i.e. Operation.Create permitted in {Role1, Role2, ..., RoleN}
                            if (!entityToRoleMap.ActionToRolesMap.TryAdd(actionOp, new List<string>(new string[] { role })))
                            {
                                entityToRoleMap.ActionToRolesMap[actionOp].Add(role);
                            }

                            foreach (string allowedColumn in allowedColumns)
                            {
                                entityToRoleMap.FieldToRolesMap.TryAdd(key: allowedColumn, CreateActionToRoleMap());
                                entityToRoleMap.FieldToRolesMap[allowedColumn][actionOp].Add(role);
                            }

                            roleToAction.ActionToColumnMap[actionOp] = actionToColumn;
                        }

                        if (ROLE_ANONYMOUS.Equals(role, StringComparison.OrdinalIgnoreCase))
                        {
                            // Saving the allowed columns for anonymous role in case we need to copy the
                            // allowed columns for authenticated role. This reduces the time complexity
                            // for copying over permissions to authenticated role from anonymous role.
                            allowedColumnsForAnonymousRole = allowedColumns;
                        }
                    }

                    entityToRoleMap.RoleToActionMap[role] = roleToAction;
                }

                // Check if anonymous role is defined but authenticated is not. If that is the case,
                // then the authenticated role derives permissions that are atleast equal to anonymous role.
                if (entityToRoleMap.RoleToActionMap.ContainsKey(ROLE_ANONYMOUS) &&
                    !entityToRoleMap.RoleToActionMap.ContainsKey(ROLE_AUTHENTICATED))
                {
                    CopyOverPermissionsFromAnonymousToAuthenticatedRole(entityToRoleMap, allowedColumnsForAnonymousRole);
                }

                EntityPermissionsMap[entityName] = entityToRoleMap;
            }
        }

        /// <summary>
        /// Helper method to copy over permissions from anonymous role to authenticated role in the case
        /// when anonymous role is defined for an entity in the config but authenticated role is not.
        /// </summary>
        /// <param name="entityToRoleMap">The EntityMetadata for the entity for which we want to copy permissions
        /// from anonymous to authenticated role.</param>
        /// <param name="allowedColumnsForAnonymousRole">List of allowed columns for anonymous role.</param>
        private static void CopyOverPermissionsFromAnonymousToAuthenticatedRole(
            EntityMetadata entityToRoleMap,
            HashSet<string> allowedColumnsForAnonymousRole)
        {
            // Using assignment operator overrides the existing value for the key /
            // adds a new entry for (key,value) pair if absent, to the map.
            entityToRoleMap.RoleToActionMap[ROLE_AUTHENTICATED] = entityToRoleMap.RoleToActionMap[ROLE_ANONYMOUS];

            // Copy over ActionToRolesMap for authenticated role from anonymous role.
            Dictionary<Operation, ActionMetadata> allowedActionMap =
                entityToRoleMap.RoleToActionMap[ROLE_ANONYMOUS].ActionToColumnMap;
            foreach (Operation operation in allowedActionMap.Keys)
            {
                entityToRoleMap.ActionToRolesMap[operation].Add(ROLE_AUTHENTICATED);
            }

            // Copy over FieldToRolesMap for authenticated role from anonymous role.
            foreach (string allowedColumnInAnonymousRole in allowedColumnsForAnonymousRole)
            {
                Dictionary<Operation, List<string>> allowedOperationsForField =
                    entityToRoleMap.FieldToRolesMap[allowedColumnInAnonymousRole];
                foreach (Operation operation in allowedOperationsForField.Keys)
                {
                    if (allowedOperationsForField[operation].Contains(ROLE_ANONYMOUS))
                    {
                        allowedOperationsForField[operation].Add(ROLE_AUTHENTICATED);
                    }
                }
            }
        }

        /// <summary>
        /// Helper method to create a list consisting of the given action name.
        /// In case the action is a wildcard(*), it gets resolved to a set of CRUD operations.
        /// </summary>
        /// <param name="action">Action name.</param>
        /// <returns>IEnumerable of all available action name</returns>
        public static IEnumerable<Operation> GetAllActions(Operation action)
        {
            return action is Operation.All ? Action.ValidPermissionActions : new List<Operation> { action };
        }

        /// <summary>
        /// From the given parameters, processes the included and excluded column permissions to output
        /// a list of columns that are "allowed".
        /// -- IncludedColumns minus ExcludedColumns == Allowed Columns
        /// -- Does not yet account for either being wildcard (*).
        /// </summary>
        /// <param name="allowedExposedColumns">Set of fields exposed to user.</param>
        /// <param name="entityName">Entity from request</param>
        /// <param name="allowedDBColumns">Set of allowed backing field names.</param>
        private void PopulateAllowedExposedColumns(HashSet<string> allowedExposedColumns,
            string entityName,
            HashSet<string> allowedDBColumns)
        {
            foreach (string dbColumn in allowedDBColumns)
            {
                if (_metadataProvider.TryGetExposedColumnName(entityName, backingFieldName: dbColumn, out string? exposedName))
                {
                    if (exposedName is not null)
                    {
                        allowedExposedColumns.Add(exposedName);
                    }
                }
            }
        }

        /// <inheritdoc />
        public IEnumerable<string> GetAllowedExposedColumns(string entityName, string roleName, Operation action)
        {
            return EntityPermissionsMap[entityName].RoleToActionMap[roleName].ActionToColumnMap[action].AllowedExposedColumns;
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

            string roleClaimShortName = string.Empty;
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
                    throw new DataApiBuilderException(
                        message: $"Duplicate claims are not allowed within a request.",
                        statusCode: System.Net.HttpStatusCode.Forbidden,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                        );
                }

                if (claim.Type is ClaimTypes.Role)
                {
                    roleClaimShortName = type;
                }
            }

            // Add role claim to the claimsInRequestContext as it is not added above.
            string clientRoleHeader = context.Request.Headers[CLIENT_ROLE_HEADER].ToString();
            claimsInRequestContext.Add(roleClaimShortName, new Claim(roleClaimShortName, clientRoleHeader, ClaimValueTypes.String));

            return claimsInRequestContext;
        }

        /// <summary>
        /// Helper method to substitute all the claimTypes(denoted with @claims.claimType) in
        /// the policy string with their corresponding claimValues.
        /// </summary>
        /// <param name="policy">The policy to be processed.</param>
        /// <param name="claimsInRequestContext">Dictionary holding all the claims available in the request.</param>
        /// <returns>Processed policy with claim values substituted for claim types.</returns>
        /// <exception cref="DataApiBuilderException"></exception>
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
        /// <exception cref="DataApiBuilderException"> Throws exception when the user does not possess the given claim.</exception>
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
                throw new DataApiBuilderException(
                    message: "User does not possess all the claims required to perform this action.",
                    statusCode: System.Net.HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
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
        /// <exception cref="DataApiBuilderException">Exception thrown when the claim's datatype is not supported.</exception>
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
                    throw new DataApiBuilderException(
                        message: "One or more claims have data types which are not supported yet.",
                        statusCode: System.Net.HttpStatusCode.Forbidden,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
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
        /// <param name="action">Action to lookup applicable roles</param>
        /// <returns>Collection of roles.</returns>
        public IEnumerable<string> GetRolesForAction(string entityName, Operation action)
        {
            if (EntityPermissionsMap[entityName].ActionToRolesMap.TryGetValue(action, out List<string>? roleList) && roleList is not null)
            {
                return roleList;
            }

            return new List<string>();
        }

        /// <summary>
        /// Returns the collection of roles which can perform {action} the provided field.
        /// Applicable to GraphQL field directive @authorize on ObjectType fields.
        /// </summary>
        /// <param name="entityName">EntityName whose actionMetadata will be searched.</param>
        /// <param name="field">Field to lookup action permissions</param>
        /// <param name="action">Specific action to get collection of roles</param>
        /// <returns>Collection of role names allowed to perform action on Entity's field.</returns>
        public IEnumerable<string> GetRolesForField(string entityName, string field, Operation action)
        {
            return EntityPermissionsMap[entityName].FieldToRolesMap[field][action];
        }

        /// <summary>
        /// For a given entityName, retrieve the column names on the associated table
        /// from the metadataProvider.
        /// </summary>
        /// <param name="entityName">Used to lookup table definition of specific entity</param>
        /// <returns>Collection of columns in table definition.</returns>
        private IEnumerable<string> ResolveTableDefinitionColumns(string entityName)
        {
            if (_metadataProvider.GetDatabaseType() is DatabaseType.cosmos)
            {
                return new List<string>();
            }

            return _metadataProvider.GetTableDefinition(entityName).Columns.Keys;
        }

        /// <summary>
        /// Creates new key value map of
        /// Key: ActionType
        /// Value: Collection of role names.
        /// There are only four possible actions
        /// </summary>
        /// <returns></returns>
        private static Dictionary<Operation, List<string>> CreateActionToRoleMap()
        {
            return new Dictionary<Operation, List<string>>()
            {
                { Operation.Create, new List<string>()},
                { Operation.Read, new List<string>()},
                { Operation.Update, new List<string>()},
                { Operation.Delete, new List<string>()}
            };
        }

        #endregion
    }
}
