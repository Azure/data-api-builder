using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PermissionOperation = Azure.DataApiBuilder.Config.PermissionOperation;

namespace Azure.DataApiBuilder.Service.Authorization
{
    /// <summary>
    /// Authorization stages that require passing before a request is executed
    /// against a database.
    /// </summary>
    public class AuthorizationResolver : IAuthorizationResolver
    {
        private ISqlMetadataProvider _metadataProvider;
        private ILogger<AuthorizationResolver> _logger;
        public const string WILDCARD = "*";
        public const string CLAIM_PREFIX = "@claims.";
        public const string FIELD_PREFIX = "@item.";
        public const string CLIENT_ROLE_HEADER = "X-MS-API-ROLE";
        public const string ROLE_ANONYMOUS = "anonymous";
        public const string ROLE_AUTHENTICATED = "authenticated";
        public const string INVALID_POLICY_CLAIM_MESSAGE = "One or more claims referenced in an authorization policy have value types which are not supported.";

        public Dictionary<string, EntityMetadata> EntityPermissionsMap { get; private set; } = new();

        public AuthorizationResolver(
            RuntimeConfigProvider runtimeConfigProvider,
            ISqlMetadataProvider sqlMetadataProvider,
            ILogger<AuthorizationResolver> logger
            )
        {
            _metadataProvider = sqlMetadataProvider;
            _logger = logger;
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
        public bool AreRoleAndOperationDefinedForEntity(string entityName, string roleName, Config.Operation operation)
        {
            if (EntityPermissionsMap.TryGetValue(entityName, out EntityMetadata? valueOfEntityToRole))
            {
                if (valueOfEntityToRole.RoleToOperationMap.TryGetValue(roleName, out RoleMetadata? valueOfRoleToOperation))
                {
                    if (valueOfRoleToOperation!.OperationToColumnMap.ContainsKey(operation))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        public bool AreColumnsAllowedForOperation(string entityName, string roleName, Config.Operation operation, IEnumerable<string> columns)
        {
            // Columns.Count() will never be zero because this method is called after a check ensures Count() > 0
            Assert.IsFalse(columns.Count() == 0, message: "columns.Count() should be greater than 0.");

            OperationMetadata operationToColumnMap = EntityPermissionsMap[entityName].RoleToOperationMap[roleName].OperationToColumnMap[operation];

            // Each column present in the request is an "exposedColumn".
            // Authorization permissions reference "backingColumns"
            // Resolve backingColumn name to check authorization.
            // Failure indicates that request contain invalid exposedColumn for entity.
            foreach (string exposedColumn in columns)
            {
                if (_metadataProvider.TryGetBackingColumn(entityName, field: exposedColumn, out string? backingColumn))
                {
                    // backingColumn will not be null when TryGetBackingColumn() is true.
                    if (operationToColumnMap.Excluded.Contains(backingColumn!) ||
                        !operationToColumnMap.Included.Contains(backingColumn!))
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
        public string ProcessDBPolicy(string entityName, string roleName, Config.Operation operation, HttpContext httpContext)
        {
            string dBpolicyWithClaimTypes = GetDBPolicyForRequest(entityName, roleName, operation);

            if (string.IsNullOrWhiteSpace(dBpolicyWithClaimTypes))
            {
                return string.Empty;
            }

            return GetPolicyWithClaimValues(dBpolicyWithClaimTypes, GetAllUserClaims(httpContext));
        }

        /// <summary>
        /// Helper function to fetch the database policy associated with the current request based on the entity under
        /// action, the role defined in the the request and the operation to be executed.
        /// When no database policy is found, no database query predicates need to be added.
        /// 1) _entityPermissionMap[entityName] finds the entityMetaData for the current entityName
        /// 2) entityMetaData.RoleToOperationMap[roleName] finds the roleMetaData for the current roleName
        /// 3) roleMetaData.OperationToColumnMap[operation] finds the operationMetadata for the current operation
        /// 4) operationMetaData.databasePolicy finds the required database policy
        /// </summary>
        /// <param name="entityName">Entity from request.</param>
        /// <param name="roleName">Role defined in client role header.</param>
        /// <param name="operation">Operation type: create, read, update, delete.</param>
        /// <returns>Policy string if a policy exists in config.</returns>
        private string GetDBPolicyForRequest(string entityName, string roleName, Config.Operation operation)
        {
            if (!EntityPermissionsMap[entityName].RoleToOperationMap.TryGetValue(roleName, out RoleMetadata? roleMetadata))
            {
                return string.Empty;
            }

            if (!roleMetadata.OperationToColumnMap.TryGetValue(operation, out OperationMetadata? operationMetadata))
            {
                return string.Empty;
            }

            // Get the database policy for the specified operation.
            string? dbPolicy = operationMetadata.DatabasePolicy;

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
                    RoleMetadata roleToOperation = new();
                    object[] Operations = permission.Operations;
                    foreach (JsonElement operationElement in Operations)
                    {
                        Config.Operation operation = Config.Operation.None;
                        OperationMetadata operationToColumn = new();

                        // Use a hashset to store all the backing field names
                        // that are accessible to the user.
                        HashSet<string> allowedColumns = new();
                        IEnumerable<string> allTableColumns = ResolveEntityDefinitionColumns(entityName);

                        // Implicitly, all table columns are 'allowed' when an operationtype is a string.
                        // Since no granular field permissions exist for this operation within the current role.
                        if (operationElement.ValueKind is JsonValueKind.String)
                        {
                            string operationName = operationElement.ToString();
                            operation = AuthorizationResolver.WILDCARD.Equals(operationName) ? Config.Operation.All : Enum.Parse<Config.Operation>(operationName, ignoreCase: true);
                            operationToColumn.Included.UnionWith(allTableColumns);
                            allowedColumns.UnionWith(allTableColumns);
                        }
                        else
                        {
                            // If not a string, the operationObj is expected to be an object that can be deserialized into PermissionOperation
                            // object. We will put validation checks later to make sure this is the case.
                            if (RuntimeConfig.TryGetDeserializedJsonString(operationElement.ToString(), out PermissionOperation? operationObj, _logger)
                                && operationObj is not null)
                            {
                                operation = operationObj.Name;
                                if (operationObj.Fields is null)
                                {
                                    operationToColumn.Included.UnionWith(ResolveEntityDefinitionColumns(entityName));
                                }
                                else
                                {
                                    if (operationObj.Fields.Include is not null)
                                    {
                                        // When a wildcard (*) is defined for Included columns, all of the table's
                                        // columns must be resolved and placed in the operationToColumn Key/Value store.
                                        // This is especially relevant for find requests, where actual column names must be
                                        // resolved when no columns were included in a request.
                                        if (operationObj.Fields.Include.Count == 1 && operationObj.Fields.Include.Contains(WILDCARD))
                                        {
                                            operationToColumn.Included.UnionWith(ResolveEntityDefinitionColumns(entityName));
                                        }
                                        else
                                        {
                                            operationToColumn.Included = operationObj.Fields.Include;
                                        }
                                    }

                                    if (operationObj.Fields.Exclude is not null)
                                    {
                                        // When a wildcard (*) is defined for Excluded columns, all of the table's
                                        // columns must be resolved and placed in the operationToColumn Key/Value store.
                                        if (operationObj.Fields.Exclude.Count == 1 && operationObj.Fields.Exclude.Contains(WILDCARD))
                                        {
                                            operationToColumn.Excluded.UnionWith(ResolveEntityDefinitionColumns(entityName));
                                        }
                                        else
                                        {
                                            operationToColumn.Excluded = operationObj.Fields.Exclude;
                                        }
                                    }
                                }

                                if (operationObj.Policy is not null && operationObj.Policy.Database is not null)
                                {
                                    operationToColumn.DatabasePolicy = operationObj.Policy.Database;
                                }

                                // Calculate the set of allowed backing column names.
                                allowedColumns.UnionWith(operationToColumn.Included.Except(operationToColumn.Excluded));
                            }
                        }

                        // Populate allowed exposed columns for each entity/role/operation combination during startup,
                        // so that it doesn't need to be evaluated per request.
                        PopulateAllowedExposedColumns(operationToColumn.AllowedExposedColumns, entityName, allowedColumns);

                        IEnumerable<Config.Operation> operations = GetAllOperations(operation);
                        foreach (Config.Operation crudOperation in operations)
                        {
                            // Try to add the opElement to the map if not present.
                            // Builds up mapping: i.e. Operation.Create permitted in {Role1, Role2, ..., RoleN}
                            if (!entityToRoleMap.OperationToRolesMap.TryAdd(crudOperation, new List<string>(new string[] { role })))
                            {
                                entityToRoleMap.OperationToRolesMap[crudOperation].Add(role);
                            }

                            foreach (string allowedColumn in allowedColumns)
                            {
                                entityToRoleMap.FieldToRolesMap.TryAdd(key: allowedColumn, CreateOperationToRoleMap());
                                entityToRoleMap.FieldToRolesMap[allowedColumn][crudOperation].Add(role);
                            }

                            roleToOperation.OperationToColumnMap[crudOperation] = operationToColumn;
                        }

                        if (ROLE_ANONYMOUS.Equals(role, StringComparison.OrdinalIgnoreCase))
                        {
                            // Saving the allowed columns for anonymous role in case we need to copy the
                            // allowed columns for authenticated role. This reduces the time complexity
                            // for copying over permissions to authenticated role from anonymous role.
                            allowedColumnsForAnonymousRole = allowedColumns;
                        }
                    }

                    entityToRoleMap.RoleToOperationMap[role] = roleToOperation;
                }

                // Check if anonymous role is defined but authenticated is not. If that is the case,
                // then the authenticated role derives permissions that are atleast equal to anonymous role.
                if (entityToRoleMap.RoleToOperationMap.ContainsKey(ROLE_ANONYMOUS) &&
                    !entityToRoleMap.RoleToOperationMap.ContainsKey(ROLE_AUTHENTICATED))
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
            entityToRoleMap.RoleToOperationMap[ROLE_AUTHENTICATED] = entityToRoleMap.RoleToOperationMap[ROLE_ANONYMOUS];

            // Copy over OperationToRolesMap for authenticated role from anonymous role.
            Dictionary<Config.Operation, OperationMetadata> allowedOperationMap =
                entityToRoleMap.RoleToOperationMap[ROLE_ANONYMOUS].OperationToColumnMap;
            foreach (Config.Operation operation in allowedOperationMap.Keys)
            {
                entityToRoleMap.OperationToRolesMap[operation].Add(ROLE_AUTHENTICATED);
            }

            // Copy over FieldToRolesMap for authenticated role from anonymous role.
            foreach (string allowedColumnInAnonymousRole in allowedColumnsForAnonymousRole)
            {
                Dictionary<Config.Operation, List<string>> allowedOperationsForField =
                    entityToRoleMap.FieldToRolesMap[allowedColumnInAnonymousRole];
                foreach (Config.Operation operation in allowedOperationsForField.Keys)
                {
                    if (allowedOperationsForField[operation].Contains(ROLE_ANONYMOUS))
                    {
                        allowedOperationsForField[operation].Add(ROLE_AUTHENTICATED);
                    }
                }
            }
        }

        /// <summary>
        /// Helper method to create a list consisting of the given operation types.
        /// In case the operation is Operation.All (wildcard), it gets resolved to a set of CRUD operations.
        /// </summary>
        /// <param name="operation">operation type.</param>
        /// <returns>IEnumerable of all available operations.</returns>
        public static IEnumerable<Config.Operation> GetAllOperations(Config.Operation operation)
        {
            return operation is Config.Operation.All ? PermissionOperation.ValidPermissionOperations : new List<Config.Operation> { operation };
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
        public IEnumerable<string> GetAllowedExposedColumns(string entityName, string roleName, Config.Operation operation)
        {
            return EntityPermissionsMap[entityName].RoleToOperationMap[roleName].OperationToColumnMap[operation].AllowedExposedColumns;
        }

        /// <summary>
        /// Helper method to extract all claims available in the HttpContext's user object and add the claims
        /// to the claimsInRequestContext dictionary to be used for claimType -> claim lookups.
        /// </summary>
        /// <param name="context">HttpContext object used to extract the authenticated user's claims.</param>
        /// <returns>Dictionary with claimType -> claim mappings.</returns>
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
                // At this point, only add non-role claims to the collection and only throw an exception for duplicate non-role claims.
                if (claim.Type is not AuthenticationConfig.ROLE_CLAIM_TYPE && !claimsInRequestContext.TryAdd(claim.Type, claim))
                {
                    // If there are duplicate claims present in the request, return an exception.
                    throw new DataApiBuilderException(
                        message: "Duplicate claims are not allowed within a request.",
                        statusCode: HttpStatusCode.Forbidden,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                        );
                }
            }

            // Only add a role claim which represents the role context evaluated for the request.
            string clientRoleHeader = context.Request.Headers[CLIENT_ROLE_HEADER].ToString();
            if (identity.HasClaim(type: AuthenticationConfig.ROLE_CLAIM_TYPE, value: clientRoleHeader))
            {
                claimsInRequestContext.Add(AuthenticationConfig.ROLE_CLAIM_TYPE, new Claim(AuthenticationConfig.ROLE_CLAIM_TYPE, clientRoleHeader, ClaimValueTypes.String));
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
            processedPolicy = processedPolicy.Replace(FIELD_PREFIX, "");
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
            // Gets <claimType> from @claims.<claimType>
            string claimType = claimTypeMatch.Value.ToString().Substring(CLAIM_PREFIX.Length);
            if (claimsInRequestContext.TryGetValue(claimType, out Claim? claim))
            {
                return GetODataCompliantClaimValue(claim);
            }
            else
            {
                // User lacks a claim which is required to perform the operation.
                throw new DataApiBuilderException(
                    message: "User does not possess all the claims required to perform this operation.",
                    statusCode: System.Net.HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                    );
            }
        }

        /// <summary>
        /// Using the input parameter claim, returns the primitive literal from claim.Value enclosed within parentheses:
        /// e.g. @claims.idp (string) resolves as ('azuread')
        /// e.g. @claims.iat (int) resolves as (1537231048)
        /// e.g. @claims.email_verified (boolean) resolves as (true)
        /// To adhere with OData 4.01 ABNF construction rules (Section 7: Literal Data Values)
        /// - Primitive string literals in URLS must be enclosed within single quotes.
        /// - Other primitive types are represented as plain values and do not require single quotes.
        /// Note: With many access token issuers, token claims are strings or string representations
        /// of other data types such as dates and GUIDs.
        /// Note: System.Security.Claim.ValueType defaults to ClaimValueTypes.String if the code calling
        /// the constructor for Claim does not explicitly provide a value type. 
        /// </summary>
        /// <param name="claim">The claim whose value is to be returned.</param>
        /// <returns>Processed claim value based on its data type.</returns>
        /// <exception cref="DataApiBuilderException">Exception thrown when the claim's datatype is not supported.</exception>
        /// <seealso cref="http://docs.oasis-open.org/odata/odata/v4.01/cs01/abnf/odata-abnf-construction-rules.txt"/>
        /// <seealso cref="https://www.iana.org/assignments/jwt/jwt.xhtml#claims"/>
        /// <seealso cref="https://www.rfc-editor.org/rfc/rfc7519.html#section-4"/>
        /// <seealso cref="https://github.com/microsoft/referencesource/blob/dae14279dd0672adead5de00ac8f117dcf74c184/mscorlib/system/security/claims/Claim.cs#L107"/>
        private static string GetODataCompliantClaimValue(Claim claim)
        {
            /* An example Claim object:
             * claim.Type: "user_email"
             * claim.Value: "authz@microsoft.com"
             * claim.ValueType: "http://www.w3.org/2001/XMLSchema#string"
             */

            switch (claim.ValueType)
            {
                case ClaimValueTypes.String:
                    return $"('{claim.Value}')";
                case ClaimValueTypes.Boolean:
                case ClaimValueTypes.Integer:
                case ClaimValueTypes.Integer32:
                case ClaimValueTypes.Integer64:
                case ClaimValueTypes.UInteger32:
                case ClaimValueTypes.UInteger64:
                case ClaimValueTypes.Double:
                    return $"({claim.Value})";
                case JsonClaimValueTypes.JsonNull:
                    return $"(null)";
                default:
                    // One of the claims in the request had unsupported data type.
                    throw new DataApiBuilderException(
                        message: INVALID_POLICY_CLAIM_MESSAGE,
                        statusCode: HttpStatusCode.Forbidden,
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
            return EntityPermissionsMap[entityName].RoleToOperationMap.Keys;
        }

        /// <summary>
        /// Returns a list of roles which define permissions for the provided operation.
        /// i.e. list of roles which allow the operation 'Read' on entityName.
        /// </summary>
        /// <param name="entityName">Entity to lookup permissions</param>
        /// <param name="operation">Operation to lookup applicable roles</param>
        /// <returns>Collection of roles.</returns>
        public IEnumerable<string> GetRolesForOperation(string entityName, Config.Operation operation)
        {
            if (EntityPermissionsMap[entityName].OperationToRolesMap.TryGetValue(operation, out List<string>? roleList) && roleList is not null)
            {
                return roleList;
            }

            return new List<string>();
        }

        /// <summary>
        /// Returns the collection of roles which can perform {operation} the provided field.
        /// Applicable to GraphQL field directive @authorize on ObjectType fields.
        /// </summary>
        /// <param name="entityName">EntityName whose operationMetadata will be searched.</param>
        /// <param name="field">Field to lookup operation permissions</param>
        /// <param name="operation">Specific operation to get collection of roles</param>
        /// <returns>Collection of role names allowed to perform operation on Entity's field.</returns>
        public IEnumerable<string> GetRolesForField(string entityName, string field, Config.Operation operation)
        {
            return EntityPermissionsMap[entityName].FieldToRolesMap[field][operation];
        }

        /// <summary>
        /// For a given entityName, retrieve the column names on the associated table
        /// from the metadataProvider.
        /// </summary>
        /// <param name="entityName">Used to lookup table definition of specific entity</param>
        /// <returns>Collection of columns in table definition.</returns>
        private IEnumerable<string> ResolveEntityDefinitionColumns(string entityName)
        {
            if (_metadataProvider.GetDatabaseType() is DatabaseType.cosmos)
            {
                return new List<string>();
            }

            // Table definition is null on stored procedure entities
            SourceDefinition? sourceDefinition = _metadataProvider.GetSourceDefinition(entityName);
            return sourceDefinition is null ? new List<string>() : sourceDefinition.Columns.Keys;
        }

        /// <summary>
        /// Creates new key value map of
        /// Key: operationType
        /// Value: Collection of role names.
        /// There are only four possible operations
        /// </summary>
        /// <returns></returns>
        private static Dictionary<Config.Operation, List<string>> CreateOperationToRoleMap()
        {
            return new Dictionary<Config.Operation, List<string>>()
            {
                { Config.Operation.Create, new List<string>()},
                { Config.Operation.Read, new List<string>()},
                { Config.Operation.Update, new List<string>()},
                { Config.Operation.Delete, new List<string>()}
            };
        }

        #endregion
    }
}
