// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Azure.DataApiBuilder.Core.Authorization;

/// <summary>
/// Authorization stages that require passing before a request is executed
/// against a database.
/// </summary>
public class AuthorizationResolver : IAuthorizationResolver
{
    private readonly RuntimeConfigProvider _runtimeConfigProvider;
    private readonly IMetadataProviderFactory _metadataProviderFactory;
    public const string WILDCARD = "*";
    public const string CLAIM_PREFIX = "@claims.";
    public const string FIELD_PREFIX = "@item.";
    public const string CLIENT_ROLE_HEADER = "X-MS-API-ROLE";
    public const string ROLE_ANONYMOUS = "anonymous";
    public const string ROLE_AUTHENTICATED = "authenticated";

    public Dictionary<string, EntityMetadata> EntityPermissionsMap { get; private set; } = new();

    public AuthorizationResolver(
        RuntimeConfigProvider runtimeConfigProvider,
        IMetadataProviderFactory metadataProviderFactory
        )
    {
        _metadataProviderFactory = metadataProviderFactory;
        if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
        {
            // Datastructure constructor will pull required properties from metadataprovider.
            SetEntityPermissionMap(runtimeConfig);
        }
        else
        {
            runtimeConfigProvider.RuntimeConfigLoadedHandlers.Add((RuntimeConfigProvider sender, RuntimeConfig config) =>
            {
                SetEntityPermissionMap(config);
                return Task.FromResult(true);
            });
        }

        _runtimeConfigProvider = runtimeConfigProvider;
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
    public bool AreRoleAndOperationDefinedForEntity(string entityIdentifier, string roleName, EntityActionOperation operation)
    {
        if (EntityPermissionsMap.TryGetValue(entityIdentifier, out EntityMetadata? valueOfEntityToRole))
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

    public bool IsStoredProcedureExecutionPermitted(string entityName, string roleName, SupportedHttpVerb httpVerb)
    {
        bool executionPermitted = EntityPermissionsMap.TryGetValue(entityName, out EntityMetadata? entityMetadata)
            && entityMetadata is not null
            && entityMetadata.RoleToOperationMap.TryGetValue(roleName, out _);
        return executionPermitted;
    }

    /// <inheritdoc />
    public bool AreColumnsAllowedForOperation(string entityName, string roleName, EntityActionOperation operation, IEnumerable<string> columns)
    {
        string dataSourceName = _runtimeConfigProvider.GetConfig().GetDataSourceNameFromEntityName(entityName);
        ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);

        if (!EntityPermissionsMap[entityName].RoleToOperationMap.TryGetValue(roleName, out RoleMetadata? roleMetadata) && roleMetadata is null)
        {
            return false;
        }

        // Short circuit when OperationMetadata lookup fails. When lookup succeeds, operationToColumnMap will be populated
        // to enable include/excluded column permissions lookups.
        if (roleMetadata.OperationToColumnMap.TryGetValue(operation, out OperationMetadata? operationToColumnMap) && operationToColumnMap is not null)
        {
            _runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig);
            // Each column present in the request is an "exposedColumn".
            // Authorization permissions reference "backingColumns"
            // Resolve backingColumn name to check authorization.
            // Failure indicates that request contain invalid exposedColumn for entity.
            foreach (string exposedColumn in columns)
            {
                if (metadataProvider.TryGetBackingColumn(entityName, field: exposedColumn, out string? backingColumn))
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
                else if (runtimeConfig is not null && runtimeConfig.Runtime.Rest.RequestBodyStrict)
                {
                    // Throw exception when we are not allowed extraneous fields in the rest request body,
                    // and no mapping exists for the given exposed field to a backing column.
                    throw new DataApiBuilderException(
                        message: "Invalid field name provided.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ExposedColumnNameMappingError
                        );
                }
            }

            return true;
        }

        // OperationMetadata lookup failed.
        return false;
    }

    /// <inheritdoc />
    public string ProcessDBPolicy(string entityName, string roleName, EntityActionOperation operation, HttpContext httpContext)
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
    private string GetDBPolicyForRequest(string entityName, string roleName, EntityActionOperation operation)
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
    public void SetEntityPermissionMap(RuntimeConfig runtimeConfig)
    {
        foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
        {
            EntityMetadata entityToRoleMap = new();

            bool isStoredProcedureEntity = entity.Source.Type is EntitySourceType.StoredProcedure;
            if (isStoredProcedureEntity)
            {
                SupportedHttpVerb[] methods;
                if (entity.Rest.Methods is not null)
                {
                    methods = entity.Rest.Methods;
                }
                else
                {
                    methods = (entity.Rest.Enabled) ? new SupportedHttpVerb[] { SupportedHttpVerb.Post } : Array.Empty<SupportedHttpVerb>();
                }

                entityToRoleMap.StoredProcedureHttpVerbs = new(methods);
            }

            // Store the allowedColumns for anonymous role.
            // In case the authenticated role is not defined on the entity,
            // this will help in copying over permissions from anonymous role to authenticated role.
            HashSet<string> allowedColumnsForAnonymousRole = new();
            string dataSourceName = runtimeConfig.GetDataSourceNameFromEntityName(entityName);
            ISqlMetadataProvider metadataProvider = _metadataProviderFactory.GetMetadataProvider(dataSourceName);
            foreach (EntityPermission permission in entity.Permissions)
            {
                string role = permission.Role;
                RoleMetadata roleToOperation = new();
                EntityAction[] entityActions = permission.Actions;
                foreach (EntityAction entityAction in entityActions)
                {
                    EntityActionOperation operation = entityAction.Action;
                    OperationMetadata operationToColumn = new();

                    // Use a HashSet to store all the backing field names
                    // that are accessible to the user.
                    HashSet<string> allowedColumns = new();
                    IEnumerable<string> allTableColumns = ResolveEntityDefinitionColumns(entityName, metadataProvider);

                    if (entityAction.Fields is null)
                    {
                        operationToColumn.Included.UnionWith(ResolveEntityDefinitionColumns(entityName, metadataProvider));
                    }
                    else
                    {
                        // When a wildcard (*) is defined for Included columns, all of the table's
                        // columns must be resolved and placed in the operationToColumn Key/Value store.
                        // This is especially relevant for find requests, where actual column names must be
                        // resolved when no columns were included in a request.
                        if (entityAction.Fields.Include is null ||
                            (entityAction.Fields.Include.Count == 1 && entityAction.Fields.Include.Contains(WILDCARD)))
                        {
                            operationToColumn.Included.UnionWith(ResolveEntityDefinitionColumns(entityName, metadataProvider));
                        }
                        else
                        {
                            operationToColumn.Included = entityAction.Fields.Include;
                        }

                        // When a wildcard (*) is defined for Excluded columns, all of the table's
                        // columns must be resolved and placed in the operationToColumn Key/Value store.
                        if (entityAction.Fields.Exclude is null ||
                            (entityAction.Fields.Exclude.Count == 1 && entityAction.Fields.Exclude.Contains(WILDCARD)))
                        {
                            operationToColumn.Excluded.UnionWith(ResolveEntityDefinitionColumns(entityName, metadataProvider));
                        }
                        else
                        {
                            operationToColumn.Excluded = entityAction.Fields.Exclude;
                        }
                    }

                    if (entityAction.Policy is not null && entityAction.Policy.Database is not null)
                    {
                        operationToColumn.DatabasePolicy = entityAction.Policy.Database;
                    }

                    // Calculate the set of allowed backing column names.
                    allowedColumns.UnionWith(operationToColumn.Included.Except(operationToColumn.Excluded));

                    // Populate allowed exposed columns for each entity/role/operation combination during startup,
                    // so that it doesn't need to be evaluated per request.
                    PopulateAllowedExposedColumns(operationToColumn.AllowedExposedColumns, entityName, allowedColumns, metadataProvider);

                    IEnumerable<EntityActionOperation> operations = GetAllOperationsForObjectType(operation, entity.Source.Type);
                    foreach (EntityActionOperation crudOperation in operations)
                    {
                        // Try to add the opElement to the map if not present.
                        // Builds up mapping: i.e. Operation.Create permitted in {Role1, Role2, ..., RoleN}
                        if (!entityToRoleMap.OperationToRolesMap.TryAdd(crudOperation, new List<string>(new string[] { role })))
                        {
                            entityToRoleMap.OperationToRolesMap[crudOperation].Add(role);
                        }

                        foreach (string allowedColumn in allowedColumns)
                        {
                            entityToRoleMap.FieldToRolesMap.TryAdd(key: allowedColumn, CreateOperationToRoleMap(entity.Source.Type));
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
        Dictionary<EntityActionOperation, OperationMetadata> allowedOperationMap =
            entityToRoleMap.RoleToOperationMap[ROLE_ANONYMOUS].OperationToColumnMap;
        foreach (EntityActionOperation operation in allowedOperationMap.Keys)
        {
            entityToRoleMap.OperationToRolesMap[operation].Add(ROLE_AUTHENTICATED);
        }

        // Copy over FieldToRolesMap for authenticated role from anonymous role.
        foreach (string allowedColumnInAnonymousRole in allowedColumnsForAnonymousRole)
        {
            Dictionary<EntityActionOperation, List<string>> allowedOperationsForField =
                entityToRoleMap.FieldToRolesMap[allowedColumnInAnonymousRole];
            foreach (EntityActionOperation operation in allowedOperationsForField.Keys)
            {
                if (allowedOperationsForField[operation].Contains(ROLE_ANONYMOUS))
                {
                    allowedOperationsForField[operation].Add(ROLE_AUTHENTICATED);
                }
            }
        }
    }

    /// <summary>
    /// Returns a list of all possible operations depending on the provided EntitySourceType.
    /// Stored procedures only support Operation.Execute.
    /// In case the operation is Operation.All (wildcard), it gets resolved to a set of CRUD operations.
    /// </summary>
    /// <param name="operation">operation type.</param>
    /// <param name="sourceType">Type of database object: Table, View, or Stored Procedure.</param>
    /// <returns>IEnumerable of all available operations.</returns>
    public static IEnumerable<EntityActionOperation> GetAllOperationsForObjectType(EntityActionOperation operation, EntitySourceType? sourceType)
    {
        if (sourceType is EntitySourceType.StoredProcedure)
        {
            return new List<EntityActionOperation> { EntityActionOperation.Execute };
        }

        return operation is EntityActionOperation.All ? EntityAction.ValidPermissionOperations : new List<EntityActionOperation> { operation };
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
    private static void PopulateAllowedExposedColumns(HashSet<string> allowedExposedColumns,
        string entityName,
        HashSet<string> allowedDBColumns,
        ISqlMetadataProvider metadataProvider)
    {
        foreach (string dbColumn in allowedDBColumns)
        {
            if (metadataProvider.TryGetExposedColumnName(entityName, backingFieldName: dbColumn, out string? exposedName))
            {
                if (exposedName is not null)
                {
                    allowedExposedColumns.Add(exposedName);
                }
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> GetAllowedExposedColumns(string entityName, string roleName, EntityActionOperation operation)
    {
        return EntityPermissionsMap[entityName].RoleToOperationMap[roleName].OperationToColumnMap[operation].AllowedExposedColumns;
    }

    /// <summary>
    /// Helper method to extract all claims available in the HttpContext's user object and add the claims
    /// to the claimsInRequestContext dictionary to be used for claimType -> claim lookups.
    /// </summary>
    /// <param name="context">HttpContext object used to extract the authenticated user's claims.</param>
    /// <returns>Dictionary with claimType -> claim mappings.</returns>
    public static Dictionary<string, Claim> GetAllUserClaims(HttpContext? context)
    {
        Dictionary<string, Claim> claimsInRequestContext = new();
        if (context is null)
        {
            return claimsInRequestContext;
        }

        string clientRoleHeader = context.Request.Headers[CLIENT_ROLE_HEADER].ToString();

        // Iterate through all the identities to populate claims in request context.
        foreach (ClaimsIdentity identity in context.User.Identities)
        {

            // Only add a role claim which represents the role context evaluated for the request,
            // as this can be via the virtue of an identity added by DAB.
            if (!claimsInRequestContext.ContainsKey(AuthenticationOptions.ROLE_CLAIM_TYPE) &&
                identity.HasClaim(type: AuthenticationOptions.ROLE_CLAIM_TYPE, value: clientRoleHeader))
            {
                claimsInRequestContext.Add(AuthenticationOptions.ROLE_CLAIM_TYPE, new Claim(AuthenticationOptions.ROLE_CLAIM_TYPE, clientRoleHeader, ClaimValueTypes.String));
            }

            // If identity is not authenticated, we don't honor any other claims present in this identity.
            if (!identity.IsAuthenticated)
            {
                continue;
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
                if (!claim.Type.Equals(AuthenticationOptions.ROLE_CLAIM_TYPE) && !claimsInRequestContext.TryAdd(claim.Type, claim))
                {
                    // If there are duplicate claims present in the request, return an exception.
                    throw new DataApiBuilderException(
                        message: "Duplicate claims are not allowed within a request.",
                        statusCode: HttpStatusCode.Forbidden,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                        );
                }
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
    /// <exception cref="DataApiBuilderException"></exception>
    private static string GetPolicyWithClaimValues(string policy, Dictionary<string, Claim> claimsInRequestContext)
    {
        // Regex used to extract all claimTypes in policy. It finds all the substrings which are
        // of the form @claims.*** where *** contains characters from a-zA-Z0-9._ .
        string claimCharsRgx = @"@claims\.[a-zA-Z0-9_\.]*";

        // Find all the claimTypes from the policy
        string processedPolicy = Regex.Replace(policy, claimCharsRgx,
            (claimTypeMatch) => GetClaimValueFromClaim(claimTypeMatch, claimsInRequestContext));

        // Remove occurrences of @item. directives
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
            return GetClaimValue(claim);
        }
        else
        {
            // User lacks a claim which is required to perform the operation.
            throw new DataApiBuilderException(
                message: "User does not possess all the claims required to perform this operation.",
                statusCode: HttpStatusCode.Forbidden,
                subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                );
        }
    }

    /// <summary>
    /// Using the input parameter claim, returns the primitive literal from claim.Value:
    /// e.g. @claims.idp (string) resolves as 'azuread'
    /// e.g. @claims.iat (int) resolves as 1537231048
    /// e.g. @claims.email_verified (boolean) resolves as true
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
    public static string GetClaimValue(Claim claim)
    {
        /* An example Claim object:
         * claim.Type: "user_email"
         * claim.Value: "authz@microsoft.com"
         * claim.ValueType: "http://www.w3.org/2001/XMLSchema#string"
         */

        switch (claim.ValueType)
        {
            case ClaimValueTypes.String:
                return $"'{claim.Value}'";
            case ClaimValueTypes.Boolean:
            case ClaimValueTypes.Integer:
            case ClaimValueTypes.Integer32:
            case ClaimValueTypes.Integer64:
            case ClaimValueTypes.UInteger32:
            case ClaimValueTypes.UInteger64:
            case ClaimValueTypes.Double:
                return $"{claim.Value}";
            case JsonClaimValueTypes.JsonNull:
                return $"null";
            default:
                // One of the claims in the request had unsupported data type.
                throw new DataApiBuilderException(
                    message: $"The claim value for claim: {claim.Type} belonging to the user has an unsupported data type.",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnsupportedClaimValueType
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
    public IEnumerable<string> GetRolesForOperation(string entityName, EntityActionOperation operation)
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
    public IEnumerable<string> GetRolesForField(string entityName, string field, EntityActionOperation operation)
    {
        // A field may not exist in FieldToRolesMap when that field is not an included column (implicitly or explicitly) in
        // any role.
        if (EntityPermissionsMap[entityName].FieldToRolesMap.TryGetValue(field, out Dictionary<EntityActionOperation, List<string>>? operationToRoles)
            && operationToRoles is not null)
        {
            if (operationToRoles.TryGetValue(operation, out List<string>? roles) && roles is not null)
            {
                return roles;
            }
        }

        return new List<string>();
    }

    /// <summary>
    /// For a given entityName, retrieve the column names on the associated table
    /// from the metadataProvider.
    /// For CosmosDb_NoSql, read all the column names from schema.gql GraphQL type fields
    /// </summary>
    /// <param name="entityName">Used to lookup table definition of specific entity</param>
    /// <returns>Collection of columns in table definition.</returns>
    private static IEnumerable<string> ResolveEntityDefinitionColumns(string entityName, ISqlMetadataProvider metadataProvider)
    {
        if (metadataProvider.GetDatabaseType() is DatabaseType.CosmosDB_NoSQL)
        {
            return metadataProvider.GetSchemaGraphQLFieldNamesForEntityName(entityName);
        }

        // Table definition is null on stored procedure entities
        SourceDefinition? sourceDefinition = metadataProvider.GetSourceDefinition(entityName);
        return sourceDefinition is null ? new List<string>() : sourceDefinition.Columns.Keys;
    }

    /// <summary>
    /// Creates new key value map of
    /// Key: operationType
    /// Value: Collection of role names.
    /// There are only five possible operations
    /// </summary>
    /// <returns>Dictionary: Key - Operation | Value - List of roles.</returns>
    private static Dictionary<EntityActionOperation, List<string>> CreateOperationToRoleMap(EntitySourceType? sourceType)
    {
        if (sourceType is EntitySourceType.StoredProcedure)
        {
            return new Dictionary<EntityActionOperation, List<string>>()
            {
                { EntityActionOperation.Execute, new List<string>()}
            };
        }

        return new Dictionary<EntityActionOperation, List<string>>()
        {
            { EntityActionOperation.Create, new List<string>()},
            { EntityActionOperation.Read, new List<string>()},
            { EntityActionOperation.Update, new List<string>()},
            { EntityActionOperation.Delete, new List<string>()}
        };
    }

    #endregion
}
