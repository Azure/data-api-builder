using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models.Authorization;
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
        private Dictionary<string, EntityMetadata> _entityPermissionMap = new();
        private const string WILDCARD = "*";
        private static readonly HashSet<string> _validActions = new() { "create", "read", "update", "delete" };

        public const string CLIENT_ROLE_HEADER = "X-MS-API-ROLE";

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
        public bool AreColumnsAllowedForAction(string entityName, string roleName, string actionName, List<string> columns)
        {
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

            foreach (string column in columns)
            {
                if (actionToColumnMap.excluded.Contains(column) || actionToColumnMap.excluded.Contains(WILDCARD) ||
                    !(actionToColumnMap.included.Contains(WILDCARD) || actionToColumnMap.included.Contains(column)))
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

            string? dbPolicy = _entityPermissionMap[entityName].RoleToActionMap[roleName].ActionToColumnMap[action].database;
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
                            actionToColumn.included.Add(WILDCARD);
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
                                    actionToColumn.included = new(actionObj.Fields.Include);
                                }

                                if (actionObj.Fields!.Exclude is not null)
                                {
                                    actionToColumn.excluded = new(actionObj.Fields.Exclude);
                                }

                                if (actionObj.Policy is not null && actionObj.Policy.Database is not null)
                                {
                                    actionToColumn.database = actionObj.Policy.Database;
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

        /// <summary>
        /// Helper method to check if the given actionName is valid/allowed.
        /// </summary>
        /// <param name="actionName">The actionName to be validated.</param>
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
            Dictionary<string, Claim> claimsInRequestContext = PopulateAllClaimsInReqCtxt(context);
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
        private static Dictionary<string, Claim> PopulateAllClaimsInReqCtxt(HttpContext context)
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
            string claimCharsRgx = @"@claims\.[^\s\)$]*";

            // Regex to check if extracted claimType is invalid. It checks if the string contains any character
            // other than a-z,A-Z,0-9,_,. .. if it does, than thats an invalid claimType.
            string invalidChars = @"[^a-zA-Z0-9_\.]+";
            Regex invalidCharsRgx = new(invalidChars, RegexOptions.Compiled);

            // Find all the claimTypes from the policy
            MatchCollection claimTypes = Regex.Matches(policy, claimCharsRgx);

            // The resultant parsed policy string is likely to be greater than the original policy
            // string, hence starting with an estimate of 2*length.
            StringBuilder policyWithClaims = new(2 * policy.Length);

            // parsedIdx indicates the index from which we need to append to the
            // resulting policy with claim values substituted.
            int parsedIdx = 0;

            foreach (Match claimType in claimTypes)
            {
                string typeOfClaim = claimType.Value;

                // Remove the prefix @claims. from the claimType
                typeOfClaim = typeOfClaim.Substring("@claims.".Length);

                if (invalidCharsRgx.IsMatch(typeOfClaim))
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

            // Check if there is still some part of policy string left to be appended to the result.
            // Append if there is any.
            if (parsedIdx < policy.Length)
            {
                policyWithClaims.Append(policy.Substring(parsedIdx));
            }

            //Remove @item. occurences from the policy string
            policyWithClaims.Replace("@item.", "");
            return policyWithClaims.ToString();
        }
        private static string GetClaimValueByDataType(Claim claim)
        {
            /* An example claim would be of format:
             * claim.Type: "user_email"
             * claim.Value: "authz@microsoft.com"
             * claim.ValueType: "string"
             */

            string claimValue = claim.Value;
            string claimValueType = claim.ValueType;

            switch (claimValueType)
            {
                case ClaimValueTypes.String:
                    return $"'{claimValue}'";
                case ClaimValueTypes.Boolean:
                case ClaimValueTypes.Integer32:
                case ClaimValueTypes.Integer64:
                case ClaimValueTypes.Double:
                    return claimValue;
                default:
                    // One of the claims in the request had unsupported data type.
                    throw new DataGatewayException(
                        message: "One or more claims have data types which are not supported yet.",
                        statusCode: System.Net.HttpStatusCode.Forbidden,
                        subStatusCode: DataGatewayException.SubStatusCodes.AuthorizationCheckFailed
                    );
            }
        }
        #endregion
    }
}
