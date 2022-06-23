using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Action = Azure.DataGateway.Config.Action;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// This class encapsulates methods to validate the runtime config file.
    /// </summary>
    public class RuntimeConfigValidator : IConfigValidator
    {
        private readonly IFileSystem _fileSystem;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private static readonly string _invalidChars = @"[^a-zA-Z0-9_\.]+";
        private static readonly Regex _invalidCharsRgx = new(_invalidChars, RegexOptions.Compiled);

        // Set of allowed actions for a request.
        private static readonly HashSet<string> _validActions = new() { ActionType.CREATE, ActionType.READ, ActionType.UPDATE, ActionType.DELETE };

        public RuntimeConfigValidator(RuntimeConfigProvider runtimeConfigProvider, IFileSystem fileSystem)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
            _fileSystem = fileSystem;
        }

        /// <summary>
        /// The driver for validation of the runtime configuration file.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public void ValidateConfig()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetRuntimeConfiguration();

            if (string.IsNullOrWhiteSpace(runtimeConfig.DatabaseType.ToString()))
            {
                throw new NotSupportedException("The database-type should be provided with the runtime config.");
            }

            if (string.IsNullOrWhiteSpace(runtimeConfig.ConnectionString))
            {
                throw new NotSupportedException($"The Connection String should be provided.");
            }

            if (runtimeConfig.DatabaseType == DatabaseType.cosmos)
            {
                if (runtimeConfig.CosmosDb is null)
                {
                    throw new NotSupportedException("CosmosDB is specified but no CosmosDB configuration information has been provided.");
                }

                if (string.IsNullOrEmpty(runtimeConfig.CosmosDb.GraphQLSchema))
                {
                    if (string.IsNullOrEmpty(runtimeConfig.CosmosDb.GraphQLSchemaPath))
                    {
                        throw new NotSupportedException("No GraphQL schema file has been provided for CosmosDB. Ensure you provide a GraphQL schema containing the GraphQL object types to expose.");
                    }

                    if (!_fileSystem.File.Exists(runtimeConfig.CosmosDb.GraphQLSchemaPath))
                    {
                        throw new FileNotFoundException($"The GraphQL schema file at '{runtimeConfig.CosmosDb.GraphQLSchemaPath}' could not be found. Ensure that it is a path relative to the runtime.");
                    }
                }
            }

            ValidateAuthenticationConfig();
            ValidateAndProcessRuntimeConfig();
        }

        private void ValidateAuthenticationConfig()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetRuntimeConfiguration();

            bool isAudienceSet =
                runtimeConfig.AuthNConfig is not null &&
                runtimeConfig.AuthNConfig.Jwt is not null &&
                !string.IsNullOrEmpty(runtimeConfig.AuthNConfig.Jwt.Audience);
            bool isIssuerSet =
                runtimeConfig.AuthNConfig is not null &&
                runtimeConfig.AuthNConfig.Jwt is not null &&
                !string.IsNullOrEmpty(runtimeConfig.AuthNConfig.Jwt.Issuer);
            if (!runtimeConfig.IsEasyAuthAuthenticationProvider() && (!isAudienceSet || !isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer must be set when not using EasyAuth.");
            }

            if (runtimeConfig!.IsEasyAuthAuthenticationProvider() && (isAudienceSet || isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer should not be set and are not used with EasyAuth.");
            }
        }

        /// <summary>
        /// Method to perform all the different validations related to the semantic correctness of the
        /// runtime configuration.
        /// </summary>
        /// <exception cref="DataGatewayException">Throws exception whenever some validation fails.</exception>
        private void ValidateAndProcessRuntimeConfig()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetRuntimeConfiguration();
            foreach ((string entityName,Entity entity) in runtimeConfig.Entities)
            {
                foreach (PermissionSetting permissionSetting in entity.Permissions)
                {
                    Object[] actions = permissionSetting.Actions;

                    // processedActions will contain the processed actions which are formed after performing all kind of
                    // validations and pre-processing.
                    List<Object> processedActions = new();
                    
                    foreach (Object action in actions)
                    {
                        string actionName;
                        if (((JsonElement)action).ValueKind == JsonValueKind.String)
                        {
                            actionName = action.ToString()!;
                            // If we have reached this point, it means that we don't have any invalid
                            // data type in actions. However we need to ensure that the actionName is valid.
                            if (!IsValidActionName(actionName))
                            {
                                // If the actionName is invalid, we throw an appropriate exception for the same.
                                throw new DataGatewayException(
                                        message: $"One of the action specified for entity:{entityName} is not valid.",
                                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                                        subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
                            }
                            
                            processedActions.Add(action);
                        }
                        else
                        {
                            Action configAction;
                            try
                            {
                                configAction = JsonSerializer.Deserialize<Config.Action>(action.ToString()!)!;

                            }
                            catch
                            {
                                throw new DataGatewayException(
                                    message: $"One of the action specified for entity:{entityName} is not well formed.",
                                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
                            }

                            actionName = configAction.Name;

                            // If we have reached this point, it means that we don't have any invalid
                            // data type in actions. However we need to ensure that the actionName is valid.
                            if (!IsValidActionName(actionName))
                            {
                                // If the actionName is invalid, we throw an appropriate exception for the same.
                                throw new DataGatewayException(
                                        message: $"One of the action specified for entity:{entityName}, role:{permissionSetting.Role} is not valid.",
                                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                                        subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
                            }

                            // Check if the IncludeSet/ExcludeSet contain wildcard. If they contain wildcard, we make sure that they
                            // don't contain any other field. If they do, we throw an appropriate exception.
                            if (configAction.Fields!.Include.Contains("*") && configAction.Fields.Include.Count > 1 ||
                                configAction.Fields.Exclude.Contains("*") && configAction.Fields.Exclude.Count > 1)
                            {
                                string incExc = configAction.Fields.Include.Contains("*") && configAction.Fields.Include.Count > 1 ? "included" : "excluded";
                                throw new DataGatewayException(
                                        message: $"No other field can be present with wildcard in the {incExc} set for: entity:{entityName}," +
                                                 $" role:{permissionSetting.Role}, action:{actionName}",
                                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                                        subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
                            }

                            if (configAction.Policy is not null && configAction.Policy.Database is not null)
                            {
                                // validate that all the fields mentioned in database policy are accessible to user
                                // and remove all the occurences of @item. directive from the policy.
                                configAction.Policy.Database = ProcessFieldsInPolicy(configAction.Policy.Database,
                                    configAction.Fields.Include, configAction.Fields.Exclude);

                                // validate that all the claimTypes in the policy are well formed, and remove
                                // parenthesis around claimTypes.
                                configAction.Policy.Database = ProcessClaimsInPolicy(configAction.Policy.Database);
                            }

                            //processedActions.Add(JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize<Action>(configAction))!);
                            processedActions.Add(JsonSerializer.SerializeToElement(configAction));
                        }
                    }

                    // Update the permissionsetting.Actions to point to the processedActions.
                    permissionSetting.Actions = processedActions.ToArray();
                }
            }
        }

        /// <summary>
        /// Helper method which takes in raw policy and returns the processed policy
        /// without @item. directives before field names.
        /// </summary>
        /// <param name="policy">Raw database policy</param>
        /// <param name="include">Array of fields which are accessible to the user.</param>
        /// <param name="exclude">Array of fields which are not accessible to the user.</param>
        /// <returns>Processed policy without @item. directives before fields names.</returns>
        private static string ProcessFieldsInPolicy(string policy, HashSet<string> includedFields, HashSet<string> excludedFields)
        {
            string fieldCharsRgx = @"@item\.[a-zA-Z0-9_]*";

            // processedPolicy would be devoid of @item. directives, provided all the columns referenced in
            // the database policy are accessible.
            string processedPolicy = Regex.Replace(policy, fieldCharsRgx, (columnNameMatch) =>
                                                   CheckAndProcessField(columnNameMatch, includedFields, excludedFields));
            return processedPolicy;
        }

        /// <summary>
        /// Helper method which takes in raw database policy and does two things:
        /// 1. Check if the field followed by @item. directive is accessible based on include/exclude fields.
        /// 2. If the field is acessible to the user, remove the @item. directive preceding the field and return the field.
        /// </summary>
        /// <param name="columnNameMatch"></param>
        /// <param name="included">Set of fields which are accessible to the user.</param>
        /// <param name="excluded">Set of fields which are accessible to the user.</param>
        /// <returns>Field name without the @item. prefix.</returns>
        /// <exception cref="DataGatewayException">Throws exception if the field is not accessible.</exception>
        private static string CheckAndProcessField(Match columnNameMatch,HashSet<string> includedFields,HashSet<string> excludedFields)
        {
            string columnName = columnNameMatch.Value.Substring("@item.".Length);
            if (excludedFields.Contains(columnName!) || excludedFields.Contains("*") ||
                !(includedFields.Contains("*") || includedFields.Contains(columnName)))
            {
                // If column is present in excluded OR excluded='*'
                // If column is absent from included and included!=*
                // In this case, the column is not accessible to the user
                throw new DataGatewayException(
                    message: $"Not all the columns required by policy are accessible.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            return columnName;
        }

        private static string ProcessClaimsInPolicy(string policy)
        {
            // Regex used to extract all claimTypes in policy. It finds all the substrings which are
            // of the form @claims.*** delimited by space character,end of the line or end of the string.
            string claimCharsRgx = @"@claims\.[^\s\)]*";
            StringBuilder processedPolicy = new();

            // Pre-process the policy to replace "( " with "(", i.e. remove
            // extra spaces after opening parenthesis. This will prevent allowed claimTypes
            // from being invalidated.
            string reduntantSpaceRgx = @"\(\s*";
            policy = Regex.Replace(policy, reduntantSpaceRgx, "(");
            // Find all the claimTypes from the policy
            MatchCollection claimTypes = Regex.Matches(policy, claimCharsRgx);

            // parsedIdx indicates the last index in the policy string from which we need to append to the
            // processedPolicy.
            int parsedIdx = 0;

            foreach (Match claimType in claimTypes)
            {
                // Remove the prefix @claims. from the claimType
                string typeOfClaimWithOpenParenthesis = claimType.Value.Substring("@claims.".Length);

                //Process typeOfClaimWithParenthesis to remove opening parenthesis.
                string typeOfClaim = GetClaimTypeWithoutOpeningParenthesis(typeOfClaimWithOpenParenthesis);

                if (string.IsNullOrWhiteSpace(typeOfClaim))
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

                int claimIdx = claimType.Index;

                // Add token for the portion of policy string between the current and the previous @claims.*** claimType
                // to the processedPolicy.
                processedPolicy.Append(policy.Substring(parsedIdx, claimIdx - parsedIdx));

                // Add token for the claimType to processedPolicy
                processedPolicy.Append("@claims."+typeOfClaim);

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

            if (parsedIdx < policy.Length)
            {
                // Append if there is still some part of policy string left to be appended to the result.
                processedPolicy.Append(policy.Substring(parsedIdx));
            }

            return processedPolicy.ToString();
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
        /// Returns whether the actionName is a valid
        /// - Create, Read, Update, Delete (CRUD) operation
        /// - Wildcard (*)
        /// </summary>
        /// <param name="actionName"></param>
        /// <returns></returns>
        public static bool IsValidActionName(string actionName)
        {
            return actionName.Equals("*") || _validActions.Contains(actionName);
        }
    }
}
