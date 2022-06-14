using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        // Regex to check if extracted claimType is invalid. It checks if the string contains any character
        // other than a-z,A-Z,0-9,_,. .. if it does, than thats an invalid claimType.
        private static readonly string _invalidChars = @"[^a-zA-Z0-9_\.]+";
        private static readonly Regex _invalidCharsRgx = new(_invalidChars, RegexOptions.Compiled);

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
        public string TryProcessDBPolicy(string entityName, string roleName, string action, HttpContext httpContext)
        {
            string dBpolicyWithClaimTypes = GetDBPolicyForRequest(entityName, roleName, action);
            return string.IsNullOrWhiteSpace(dBpolicyWithClaimTypes) ? string.Empty :
                   ProcessTokenClaimsForPolicy(dBpolicyWithClaimTypes, httpContext);
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
            // actionMetaData.database finds the required database policy

            string? dbPolicy = _entityPermissionMap[entityName].RoleToActionMap[roleName].ActionToColumnMap[action].databasePolicy;
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
                        if (actionElement.ValueKind == JsonValueKind.String)
                        {
                            actionName = actionElement.ToString();
                            actionToColumn.included.UnionWith(ResolveTableDefinitionColumns(entityName));
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
                                    actionToColumn.excluded = new(actionObj.Fields.Exclude);
                                }

                                if (actionObj.Policy is not null && actionObj.Policy.Database is not null)
                                {
                                    actionToColumn.databasePolicy = actionObj.Policy.Database;
                                }
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
            IEnumerable<string> allowedDBColumns = actionMetadata.included.Except(actionMetadata.excluded);
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
            return actionName.Equals(WILDCARD) || _validActions.Contains(actionName);
        }

        /// <summary>
        /// Helper method to process the given policy obtained from config, and convert it into an injectable format in
        /// the HttpContext object by substituting/removing @claim./@item. directives.
        /// </summary>
        /// <param name="policy">The policy to be processed.</param>
        /// <param name="context">HttpContext object used to extract all the claims available in the request.</param>
        /// <returns>Processed policy string that can be injected into the HttpContext object.</returns>
        private static string ProcessTokenClaimsForPolicy(string policy, HttpContext context)
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
                string type = claim.Type;

                if (!claimsInRequestContext.ContainsKey(type))
                {
                    claimsInRequestContext.Add(type, claim);
                }
                else
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
        /// Helper method to substitute all the claimTypes in the policy string with their corresponding
        /// claimValues.
        /// </summary>
        /// <param name="policy">The policy to be processed.</param>
        /// <param name="claimsInRequestContext">Dictionary holding all the claims available in the request.</param>
        /// <returns>Processed policy with claim values substituted for claim types.</returns>
        /// <exception cref="DataGatewayException"></exception>
        private static string GetPolicyWithClaimValues(string policy, Dictionary<string, Claim> claimsInRequestContext)
        {
            // Regex used to extract all claimTypes in policy. It finds all the substrings which are
            // of the form @claims.*** delimited by space character,end of the line or end of the string.
            string claimCharsRgx = @"@claims\.[^\s\)]*";

            // Pre-process the policy to replace "( " with "(", i.e. remove
            // extra spaces after opening parenthesis. This will prevent allowed claimTypes
            // from being invalidated.
            string reduntantSpaceRgx = @"\(\s*";
            policy = Regex.Replace(policy, reduntantSpaceRgx, "(");
            // Find all the claimTypes from the policy
            MatchCollection claimTypes = Regex.Matches(policy, claimCharsRgx);

            // The resultant parsed policy string is likely to be greater than the original policy
            // string, hence starting with an estimate of 2*length.
            StringBuilder policyWithClaims = new(2 * policy.Length);

            // parsedIdx indicates the last index in the policy string from which we need to append to the
            // resulting policy.
            int parsedIdx = 0;

            foreach (Match claimType in claimTypes)
            {
                // Remove the prefix @claims. from the claimType
                string typeOfClaimWithOpenParenthesis = claimType.Value.Substring("@claims.".Length);

                //Process typeOfClaimWithParenthesis to remove opening parenthesis.
                string typeOfClaim = GetClaimTypeWithoutOpeningParenthesis(typeOfClaimWithOpenParenthesis);

                if (string.Empty.Equals(typeOfClaim))
                {
                    // Empty claimType is not allowed
                    throw new DataGatewayException(
                        message: $"Invalid format for claim type {typeOfClaim} supplied in policy.",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError
                        );
                }

                if (_invalidCharsRgx.IsMatch(typeOfClaim))
                {
                    // Not a valid claimType containing allowed characters
                    throw new DataGatewayException(
                        message: $"Invalid format for claim type {typeOfClaim} supplied in policy.",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError
                        );
                }

                if (claimsInRequestContext.TryGetValue(typeOfClaim, out Claim? claim))
                {
                    int claimIdx = claimType.Index;

                    // Append the portion of policy string between the current and the previous @claims.*** claimType
                    // to the resulting policy string
                    policyWithClaims.Append(policy.Substring(parsedIdx, claimIdx - parsedIdx));

                    // Append the claimValue to the resulting policy string
                    policyWithClaims.Append(GetClaimValueByDataType(claim));

                    // Move the parsedIdx to the index following a claimType in the policy string
                    parsedIdx = claimIdx + claimType.Value.Length;

                    // Expected number of closing parenthesis after the claimType,
                    // equal to the number of opening parenthesis before the claimType.
                    int expNumClosingParenthesis = typeOfClaimWithOpenParenthesis.Length - typeOfClaim.Length;

                    // Ensure that there are atleast expectedNumClosingParenthesis following
                    // a claim type. However we don't need to include unnecessary parenthesis
                    // in our parsed policy, so we don't append.
                    while (expNumClosingParenthesis > 0)
                    {
                        if (parsedIdx >= policy.Length || (policy[parsedIdx] != ')' && policy[parsedIdx] != ' '))
                        {
                            // No. of closing parenthesis is less than opening parenthesis,
                            // which does not form a valid claimType.
                            throw new DataGatewayException(
                                message: $"Invalid format for claim type {typeOfClaim} supplied in policy.",
                                statusCode: System.Net.HttpStatusCode.InternalServerError,
                                subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError
                                );
                        }

                        // If the code reaches here, either the character is ')' or ' '.
                        // If its a ' ', we ignore as it is an extra space.
                        // If its a ')', we decrement the required closing parenthesis by 1.
                        if (policy[parsedIdx] == ')')
                        {
                            expNumClosingParenthesis--;
                        }

                        parsedIdx++;
                    }
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

            if (parsedIdx < policy.Length)
            {
                // Append if there is still some part of policy string left to be appended to the result.
                policyWithClaims.Append(policy.Substring(parsedIdx));
            }

            //Remove @item. occurences from the policy string
            policyWithClaims.Replace("@item.", "");

            return policyWithClaims.ToString();
        }

        /// <summary>
        /// Helper method to extract the claimType without opening parenthesis from
        /// the typeOfClaimWithParenthesis.
        /// </summary>
        /// <param name="typeOfClaimWithParenthesis">The claimType which potentially has opening parenthesis</param>
        /// <returns>claimType without opening parenthesis</returns>
        private static string GetClaimTypeWithoutOpeningParenthesis(string typeOfClaimWithParenthesis)
        {
            // Find the index of first non parenthesis character in the claimType
            int idx = 0;
            while (idx < typeOfClaimWithParenthesis.Length && typeOfClaimWithParenthesis[idx] == '(')
            {
                idx++;
            }

            return typeOfClaimWithParenthesis.Substring(idx);
        }

        /// <summary>
        /// Helper function to return the claim value alongwith the required additonal
        /// quotes if required. This makes sure we adhere to JSON specifications where
        /// strings are enclosed in single quotes while int,bool,double etc are not.
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
                    return $"'{claim.Value}'";
                case ClaimValueTypes.Boolean:
                case ClaimValueTypes.Integer32:
                case ClaimValueTypes.Integer64:
                case ClaimValueTypes.Double:
                    return claim.Value;
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
