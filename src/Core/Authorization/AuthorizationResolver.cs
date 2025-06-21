// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.JsonWebTokens;

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
    public const string AUTHORIZATION_HEADER = "Authorization";
    public const string ROLE_ANONYMOUS = "anonymous";
    public const string ROLE_AUTHENTICATED = "authenticated";

    public Dictionary<string, EntityMetadata> EntityPermissionsMap { get; private set; } = new();

    public AuthorizationResolver(
        RuntimeConfigProvider runtimeConfigProvider,
        IMetadataProviderFactory metadataProviderFactory,
        HotReloadEventHandler<HotReloadEventArgs>? handler = null)
    {
        _metadataProviderFactory = metadataProviderFactory;
        _runtimeConfigProvider = runtimeConfigProvider;
        handler?.Subscribe(DabConfigEvents.AUTHZ_RESOLVER_ON_CONFIG_CHANGED, OnConfigChanged);

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
    }

    /// <summary>
    /// Executed when a hot-reload event occurs. Pulls the latest
    /// runtimeconfig object from the provider and updates authorization
    /// rules used by the DAB engine.
    /// </summary>
    protected void OnConfigChanged(object? sender, HotReloadEventArgs args)
    {
        SetEntityPermissionMap(_runtimeConfigProvider.GetConfig());
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
                else if (runtimeConfig is not null && runtimeConfig.IsRequestBodyStrict)
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

        return GetPolicyWithClaimValues(dBpolicyWithClaimTypes, GetAllAuthenticatedUserClaims(httpContext));
    }

    /// <inheritdoc />
    public string GetDBPolicyForRequest(string entityName, string roleName, EntityActionOperation operation)
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

    /// <summary>
    ///  Helper method to get the role with which the GraphQL API request was executed.
    /// </summary>
    /// <param name="context">HotChocolate context for the GraphQL request.</param>
    /// <returns>Role of the current GraphQL API request.</returns>
    /// <exception cref="DataApiBuilderException">Throws exception when no client role could be inferred from the context.</exception>
    public static string GetRoleOfGraphQLRequest(IMiddlewareContext context)
    {
        string role = string.Empty;
        if (context.ContextData.TryGetValue(key: CLIENT_ROLE_HEADER, out object? value) && value is StringValues stringVals)
        {
            role = stringVals.ToString();
        }

        if (string.IsNullOrEmpty(role))
        {
            throw new DataApiBuilderException(
                message: "No ClientRoleHeader available to perform authorization.",
                statusCode: HttpStatusCode.Forbidden,
                subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
        }

        return role;
    }

    #region Helpers
    /// <summary>
    /// Method to read in data from the config class into a Dictionary for quick lookup
    /// during runtime.
    /// </summary>
    /// <param name="runtimeConfig"></param>
    private void SetEntityPermissionMap(RuntimeConfig runtimeConfig)
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
    private static void PopulateAllowedExposedColumns(
        HashSet<string> allowedExposedColumns,
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
    /// Returns a dictionary (string, string) where a key is a claim's name and a value is a claim's value.
    /// Resolves multiple claim objects of the same claim type into a JSON array mirroring the format
    /// of the claim in the original JWT token. The JSON array's type depends on the value type
    /// present in the original JWT token.
    /// </summary>
    /// <remarks>
    /// DotNet will resolve a claim with value type JSON array to multiple Claim objects
    /// with the same type and different values.
    /// e.g. roles and groups claim, which are arrays of strings and are flattened to:
    /// roles: role1, roles: role2, groups: group1, groups: group2
    /// "Claims are name valued pairs and nothing more."
    /// Ref: https://github.com/dotnet/aspnetcore/issues/13647#issuecomment-527523224
    /// The library that parses the JWT token into claims is the one that decides
    /// *how* to resolve the claims from the token's JSON payload.
    /// dotnet flattens the claims into a list:
    /// https://github.com/dotnet/aspnetcore/blob/282bfc1b486ae235a3395150a8d53073a57b7f43/src/Security/Authentication/OAuth/src/JsonKeyClaimAction.cs#L39-L53
    /// </remarks>
    /// <param name="context">HttpContext which contains a ClaimsPrincipal</param>
    /// <returns>Processed claims and claim values.</returns>
    public static Dictionary<string, string> GetProcessedUserClaims(HttpContext? context)
    {
        Dictionary<string, string> processedClaims = new();

        if (context is null)
        {
            return processedClaims;
        }

        Dictionary<string, List<Claim>> userClaims = GetAllAuthenticatedUserClaims(context);

        foreach ((string claimName, List<Claim> claimValues) in userClaims)
        {
            // Some identity providers (other than Entra ID) may emit a 'scope' claim as a JSON string array. dotnet will
            // create claim objects for each value in the array. DAB will honor that format
            // and processes the 'scope' claim objects as a JSON array serialized to a string.
            if (claimValues.Count > 1)
            {
                switch (claimValues.First().ValueType)
                {
                    case ClaimValueTypes.Boolean:
                        processedClaims.Add(claimName, value: JsonSerializer.Serialize(claimValues.Select(claim => bool.Parse(claim.Value))));
                        break;
                    case ClaimValueTypes.Integer:
                    case ClaimValueTypes.Integer32:
                        processedClaims.Add(claimName, value: JsonSerializer.Serialize(claimValues.Select(claim => int.Parse(claim.Value))));
                        break;
                    // Per Microsoft Docs: UInt32's CLS compliant alternative is Integer64
                    // https://learn.microsoft.com/dotnet/api/system.uint32#remarks
                    case ClaimValueTypes.UInteger32:
                    case ClaimValueTypes.Integer64:
                        processedClaims.Add(claimName, value: JsonSerializer.Serialize(claimValues.Select(claim => long.Parse(claim.Value))));
                        break;
                    // Per Microsoft Docs: UInt64's CLS compliant alternative is decimal
                    // https://learn.microsoft.com/dotnet/api/system.uint64#remarks
                    case ClaimValueTypes.UInteger64:
                        processedClaims.Add(claimName, value: JsonSerializer.Serialize(claimValues.Select(claim => decimal.Parse(claim.Value))));
                        break;
                    case ClaimValueTypes.Double:
                        processedClaims.Add(claimName, value: JsonSerializer.Serialize(claimValues.Select(claim => double.Parse(claim.Value))));
                        break;
                    case ClaimValueTypes.String:
                    case JsonClaimValueTypes.JsonNull:
                    case JsonClaimValueTypes.Json:
                    default:
                        string json = JsonSerializer.Serialize(claimValues.Select(claim => claim.Value));
                        processedClaims.Add(claimName, value: json);
                        break;
                }
            }
            else
            {
                // Remaining claims will be collected as string scalar values.
                // While Claim.ValueType may indicate the token value was not a string (int, bool),
                // resolving the actual type here would be a breaking change because 
                // DAB has historically sent single instance claims as value type string.
                // This block also accommodates Entra ID access tokens to avoid a breaking change because
                // the 'scp' claim is a space delimited string which is not broken up into separate claim objects
                // by dotnet. The Entra ID 'scp' claim should be passed to MSSQL's session context as-is.
                // https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference#payload-claims
                processedClaims.Add(claimName, value: claimValues[0].Value);
            }
        }

        return processedClaims;
    }

    /// <summary>
    /// Helper method to extract all claims available in the HttpContext's user object (from authenticated ClaimsIdentity objects)
    /// and add the claims to the claimsInRequestContext dictionary to be used for claimType -> claim lookups.
    /// This method only resolves one `roles` claim from the authenticated user's claims: the `roles` claim whose
    /// value matches the x-ms-api-role` header value.
    /// </summary>
    /// <remarks>
    /// DotNet will resolve a claim with value type JSON array to multiple Claim objects with the same type and different values.
    /// e.g. roles and groups claim, which are arrays of strings and are flattened to: roles: role1, roles: role2, groups: group1, groups: group2
    /// Claims are name valued pairs and nothing more. https://github.com/dotnet/aspnetcore/issues/13647#issuecomment-527523224
    /// The library that parses the jwt token into claims is the one that decides *how* to resolve the claims from the token's JSON payload.
    /// DotNet flattens the claims into a list: https://github.com/dotnet/aspnetcore/blob/282bfc1b486ae235a3395150a8d53073a57b7f43/src/Security/Authentication/OAuth/src/JsonKeyClaimAction.cs#L39-L53
    /// </remarks>
    /// <param name="context">HttpContext object used to extract the authenticated user's claims.</param>
    /// <returns>Dictionary with claimType -> list of claim mappings.</returns>
    public static Dictionary<string, List<Claim>> GetAllAuthenticatedUserClaims(HttpContext? context)
    {
        Dictionary<string, List<Claim>> resolvedClaims = new();
        if (context is null)
        {
            return resolvedClaims;
        }

        string clientRoleHeader = context.Request.Headers[CLIENT_ROLE_HEADER].ToString();

        // Iterate through all the identities to populate claims in request context.
        foreach (ClaimsIdentity identity in context.User.Identities)
        {
            // If identity is not authenticated, we don't honor any other claims present in this identity.
            if (!identity.IsAuthenticated)
            {
                continue;
            }

            // DAB will only resolve one 'roles' claim whose value matches the x-ms-api-role header value
            // because DAB executes requests in the context of a single role. The `roles` claim
            // resolved here can be forwarded to MSSQL's set-session-context. Modifying this behavior
            // is a breaking change.
            if (!resolvedClaims.ContainsKey(AuthenticationOptions.ROLE_CLAIM_TYPE) &&
                identity.HasClaim(type: AuthenticationOptions.ROLE_CLAIM_TYPE, value: clientRoleHeader))
            {
                List<Claim> roleClaim = new()
                {
                    new Claim(type: AuthenticationOptions.ROLE_CLAIM_TYPE, value: clientRoleHeader, valueType: ClaimValueTypes.String)
                };

                resolvedClaims.Add(AuthenticationOptions.ROLE_CLAIM_TYPE, roleClaim);
            }

            // Process all remaining claims adding all `Claim` objects with the same claimType (claim name)
            // into a list and storing that in resolvedClaims using the claimType as the key.
            foreach (Claim claim in identity.Claims)
            {
                // 'roles' claim has already been processed. But we preserve the original 'roles' claim.
                if (claim.Type.Equals(AuthenticationOptions.ROLE_CLAIM_TYPE))
                {
                    if (!resolvedClaims.TryAdd(AuthenticationOptions.ORIGINAL_ROLE_CLAIM_TYPE, new List<Claim>() { claim }))
                    {
                        resolvedClaims[AuthenticationOptions.ORIGINAL_ROLE_CLAIM_TYPE].Add(claim);
                    }

                    continue;
                }

                if (!resolvedClaims.TryAdd(key: claim.Type, value: new List<Claim>() { claim }))
                {
                    resolvedClaims[claim.Type].Add(claim);
                }
            }
        }

        return resolvedClaims;
    }

    /// <summary>
    /// Helper method to substitute all the claimTypes(denoted with @claims.claimType) in
    /// the policy string with their corresponding claimValues.
    /// </summary>
    /// <param name="policy">The policy to be processed.</param>
    /// <param name="claimsInRequestContext">Dictionary holding all the claims available in the request.</param>
    /// <returns>Processed policy with claim values substituted for claim types.</returns>
    /// <exception cref="DataApiBuilderException"></exception>
    private static string GetPolicyWithClaimValues(string policy, Dictionary<string, List<Claim>> claimsInRequestContext)
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
    /// <returns>The claim value of the first claim whose claimType matches 'claimTypeMatch'.</returns>
    /// <exception cref="DataApiBuilderException"> Throws exception when the user does not possess the given claim.</exception>
    private static string GetClaimValueFromClaim(Match claimTypeMatch, Dictionary<string, List<Claim>> claimsInRequestContext)
    {
        // Gets <claimType> from @claims.<claimType>
        string claimType = claimTypeMatch.Value.ToString().Substring(CLAIM_PREFIX.Length);
        if (claimsInRequestContext.TryGetValue(claimType, out List<Claim>? claims)
            && claims is not null && claims.Count > 0)
        {
            // Database policies do not support operators like "in" or "contains".
            // Return the first value in the list of claims.
            // This is not a breaking change since historically,
            // if the user had >1 role AND wrote a db policy to include the token '@claims.role',
            // DAB would fail the request. Now, the request won't fail, but the value
            // resolved is the first claim encountered (when there are multiple claim instances).
            return GetClaimValue(claims.First());
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
    private static string GetClaimValue(Claim claim)
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
