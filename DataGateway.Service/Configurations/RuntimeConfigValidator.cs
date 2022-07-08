using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Action = Azure.DataGateway.Config.Action;
using Microsoft.Extensions.Logging;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// This class encapsulates methods to validate the runtime config file.
    /// </summary>
    public class RuntimeConfigValidator : IConfigValidator
    {
        private readonly IFileSystem _fileSystem;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly ILogger<RuntimeConfigValidator> _logger;

        // Only characters from a-z,A-Z,0-9,.,_ are allowed to be present within the claimType.
        private static readonly string _invalidClaimChars = @"[^a-zA-Z0-9_\.]+";

        // Regex to check occurence of any character not among [a-z,A-Z,0-9,.,_] in the claimType.
        // The claimType is invalid if there is a match found.
        private static readonly Regex _invalidClaimCharsRgx = new(_invalidClaimChars, RegexOptions.Compiled);

        // Regex used to extract all claimTypes in policy. It finds all the substrings which are
        // of the form @claims.*** delimited by space character,end of the line or end of the string.
        private static readonly string _claimChars = @"@claims\.[^\s\)]*";

        // Set of allowed actions for a request.
        private static readonly HashSet<string> _validActions = new() { ActionType.CREATE, ActionType.READ, ActionType.UPDATE, ActionType.DELETE };

        public RuntimeConfigValidator(
            RuntimeConfigProvider runtimeConfigProvider,
            IFileSystem fileSystem,
            ILogger<RuntimeConfigValidator> logger)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
            _fileSystem = fileSystem;
            _logger = logger;
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
                const string databaseTypeNotSpecified =
                    "The database-type should be provided with the runtime config.";
                _logger.LogCritical(databaseTypeNotSpecified);
                throw new NotSupportedException(databaseTypeNotSpecified);
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
        /// runtime configuration, focusing on the permissions section of the entity.
        /// </summary>
        /// <exception cref="DataGatewayException">Throws exception whenever some validation fails.</exception>
        public void ValidatePermissionsInConfig(RuntimeConfig runtimeConfig)
        {
            foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
            {
                foreach (PermissionSetting permissionSetting in entity.Permissions)
                {
                    Object[] actions = permissionSetting.Actions;
                    foreach (Object action in actions)
                    {
                        string actionName;
                        if (((JsonElement)action).ValueKind == JsonValueKind.String)
                        {
                            actionName = action.ToString()!;
                            // If we have reached this point, it means that we don't have any invalid
                            // data type in actions. However we need to ensure that the actionName is valid.
                            ValidateActionName(actionName, entityName, permissionSetting.Role);
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
                                    subStatusCode: DataGatewayException.SubStatusCodes.ConfigValidationError);
                            }

                            actionName = configAction.Name;

                            // If we have reached this point, it means that we don't have any invalid
                            // data type in actions. However we need to ensure that the actionName is valid.
                            ValidateActionName(actionName, entityName, permissionSetting.Role);

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
                                        subStatusCode: DataGatewayException.SubStatusCodes.ConfigValidationError);
                            }

                            if (configAction.Policy is not null && configAction.Policy.Database is not null)
                            {
                                // validate that all the fields mentioned in database policy are accessible to user.
                                AreFieldsAccessible(configAction.Policy.Database,
                                    configAction.Fields.Include, configAction.Fields.Exclude);

                                // validate that all the claimTypes in the policy are well formed.
                                ValidateOrProcessClaimsInPolicy(configAction.Policy.Database, true);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Method to do the pre-processing needed in the permissions section of the runtimeconfig object.
        /// For eg. removing the @item. directives, checking for invalid characters in claimTypes etc.
        /// </summary>
        /// <param name="runtimeConfig">The deserialised config object obtained from the json config supplied.</param>
        public void ProcessPermissionsInConfig(RuntimeConfig runtimeConfig)
        {
            foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
            {
                foreach (PermissionSetting permissionSetting in entity.Permissions)
                {
                    Object[] actions = permissionSetting.Actions;

                    // processedActions will contain the processed actions which are formed after performing all kind of
                    // validations and pre-processing.
                    List<Object> processedActions = new();
                    foreach (Object action in actions)
                    {
                        if (((JsonElement)action).ValueKind == JsonValueKind.String)
                        {
                            processedActions.Add(action);
                        }
                        else
                        {
                            Action configAction;
                            configAction = JsonSerializer.Deserialize<Config.Action>(action.ToString()!)!;

                            if (configAction.Policy is not null && configAction.Policy.Database is not null)
                            {
                                // Remove all the occurences of @item. directive from the policy.
                                configAction.Policy.Database = ProcessFieldsInPolicy(configAction.Policy.Database);

                                // Remove redundant spaces and parenthesis around claimTypes.
                                configAction.Policy.Database = ValidateOrProcessClaimsInPolicy(configAction.Policy.Database, false);
                            }

                            processedActions.Add(JsonSerializer.SerializeToElement(configAction));
                        }
                    }

                    // Update the permissionsetting.Actions to point to the processedActions.
                    permissionSetting.Actions = processedActions.ToArray();
                }
            }
        }

        /// <summary>
        /// Helper method which takes in the database policy and returns the processed policy
        /// without @item. directives before field names.
        /// </summary>
        /// <param name="policy">Raw database policy</param>
        /// <returns>Processed policy without @item. directives before field names.</returns>
        private static string ProcessFieldsInPolicy(string policy)
        {
            string fieldCharsRgx = @"@item\.[a-zA-Z0-9_]*";

            // processedPolicy would be devoid of @item. directives.
            string processedPolicy = Regex.Replace(policy, fieldCharsRgx, (columnNameMatch) =>
            columnNameMatch.Value.Substring(AuthorizationResolver.FIELD_PREFIX.Length));
            return processedPolicy;
        }

        /// <summary>
        /// Method to do different validations/ pre-process claims in the policy.
        /// The decision to validate/preprocess is made by the isValidation boolean parameter.
        /// If isValidation is set to true, we do validation, else pre-process.
        /// </summary>
        /// <param name="policy">The policy to be validated and processed.</param>
        /// <returns>Processed policy</returns>
        /// <exception cref="DataGatewayException">Throws exception when one or the other validations fail.</exception>
        private static string ValidateOrProcessClaimsInPolicy(string policy, bool isValidation)
        {
            StringBuilder processedPolicy = new();
            policy = RemoveRedundantSpacesFromPolicy(policy);

            // Find all the claimTypes from the policy
            MatchCollection claimTypes = GetClaimTypesInPolicy(policy);

            // parsedIdx indicates the last index in the policy string from which we need to append to the
            // processedPolicy.
            int parsedIdx = 0;

            foreach (Match claimType in claimTypes)
            {
                // Remove the prefix @claims. from the claimType
                string typeOfClaimWithOpenParenthesis = claimType.Value.Substring(AuthorizationResolver.CLAIM_PREFIX.Length);

                //Process typeOfClaimWithParenthesis to remove opening parenthesis.
                string typeOfClaim = GetClaimTypeWithoutOpeningParenthesis(typeOfClaimWithOpenParenthesis);

                if (isValidation && string.IsNullOrWhiteSpace(typeOfClaim))
                {
                    // Empty claimType is not allowed
                    throw new DataGatewayException(
                        message: $"Claimtype cannot be empty.",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.ConfigValidationError
                        );
                }

                if (isValidation && _invalidClaimCharsRgx.IsMatch(typeOfClaim))
                {
                    // Not a valid claimType containing allowed characters
                    throw new DataGatewayException(
                        message: $"Invalid format for claim type {typeOfClaim} supplied in policy.",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.ConfigValidationError
                        );
                }

                int claimIdx = claimType.Index;

                if (!isValidation)
                {
                    // Add token for the portion of policy string between the current and the previous @claims.*** claimType
                    // to the processedPolicy.
                    processedPolicy.Append(policy.Substring(parsedIdx, claimIdx - parsedIdx));

                    // Add token for the claimType to processedPolicy
                    processedPolicy.Append(AuthorizationResolver.CLAIM_PREFIX + typeOfClaim);
                }

                // Move the parsedIdx to the index following a claimType in the policy string
                parsedIdx = claimIdx + claimType.Value.Length;

                // Expected number of closing parenthesis after the claimType,
                // equal to the number of opening parenthesis before the claimType.
                int expNumClosingParenthesis = typeOfClaimWithOpenParenthesis.Length - typeOfClaim.Length;

                // Ensure that there are atleast expectedNumClosingParenthesis following a claim type.
                while (expNumClosingParenthesis > 0)
                {
                    if (isValidation && (parsedIdx >= policy.Length || (policy[parsedIdx] != ')' && policy[parsedIdx] != ' ')))
                    {
                        // No. of closing parenthesis is less than opening parenthesis,
                        // which does not form a valid claimType.
                        throw new DataGatewayException(
                            message: $"Invalid format for claim type {typeOfClaim} supplied in policy.",
                            statusCode: System.Net.HttpStatusCode.InternalServerError,
                            subStatusCode: DataGatewayException.SubStatusCodes.ConfigValidationError
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
            } // MatchType claimType

            if (!isValidation && parsedIdx < policy.Length)
            {
                // Append if there is still some part of policy string left to be appended to the result.
                processedPolicy.Append(policy.Substring(parsedIdx));
            }

            return processedPolicy.ToString();
        }

        /// <summary>
        /// Helper method which takes in the policy string and checks whether all fields referenced
        /// within the policy are accessible to the user.
        /// </summary>
        /// <param name="policy">Database policy</param>
        /// <param name="include">Array of fields which are accessible to the user.</param>
        /// <param name="exclude">Array of fields which are not accessible to the user.</param>
        private static void AreFieldsAccessible(string policy, HashSet<string> includedFields, HashSet<string> excludedFields)
        {
            // Pattern of field references in the policy
            string fieldCharsRgx = @"@item\.[a-zA-Z0-9_]*";
            MatchCollection fieldNameMatches = Regex.Matches(policy, fieldCharsRgx);

            foreach (Match fieldNameMatch in fieldNameMatches)
            {
                if (!IsFieldAccessible(fieldNameMatch, includedFields, excludedFields))
                {
                    throw new DataGatewayException(
                    message: $"Not all the columns required by policy are accessible.",
                    statusCode: System.Net.HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.ConfigValidationError);
                }
            }
        }

        /// <summary>
        /// Helper method which takes in field prefixed with @item. directive and check if its
        /// accessible based on include/exclude fields.
        /// </summary>
        /// <param name="columnNameMatch"></param>
        /// <param name="included">Set of fields which are accessible to the user.</param>
        /// <param name="excluded">Set of fields which are not accessible to the user.</param>
        /// <returns>Boolean value indicating whether the field is accessible or not.</returns>
        /// <exception cref="DataGatewayException">Throws exception if the field is not accessible.</exception>
        private static bool IsFieldAccessible(Match columnNameMatch, HashSet<string> includedFields, HashSet<string> excludedFields)
        {
            string columnName = columnNameMatch.Value.Substring(AuthorizationResolver.FIELD_PREFIX.Length);
            if (excludedFields.Contains(columnName!) || excludedFields.Contains("*") ||
                !(includedFields.Contains("*") || includedFields.Contains(columnName)))
            {
                // If column is present in excluded OR excluded='*'
                // If column is absent from included and included!=*
                // In this case, the column is not accessible to the user
                return false;
            }

            return true;
        }

        /// <summary>
        /// Helper method to get all the claimTypes from the policy.
        /// </summary>
        /// <param name="policy">Policy from which the claimTypes are to be looked</param>
        /// <returns>Collection of all matches i.e. claimTypes.</returns>
        private static MatchCollection GetClaimTypesInPolicy(string policy)
        {
            return Regex.Matches(policy, _claimChars);
        }

        /// <summary>
        /// Helper method to preprocess the policy by replacing "( " with "(", i.e. remove 
        /// extra spaces after opening parenthesis. This will prevent allowed claimTypes 
        /// from being invalidated.
        /// </summary>
        /// <param name="policy"></param>
        /// <returns>Policy string without redundant spaces.</returns>
        private static string RemoveRedundantSpacesFromPolicy(string policy)
        {
            // Pre-process the policy to replace "( " with "(", i.e. remove
            // extra spaces after opening parenthesis. This will prevent allowed claimTypes
            // from being invalidated.
            string reduntantSpaceRgx = @"\(\s*";
            return Regex.Replace(policy, reduntantSpaceRgx, "(");
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
        /// Helper function to throw an exception if the actionName is not valid.
        /// </summary>
        /// <param name="actionName">The actionName to be validated.</param>
        /// <param name="entityName">Name of the entity on which the action is to be executed.</param>
        /// <exception cref="DataGatewayException">Exception thrown if the actionName is invalid.</exception>
        private static void ValidateActionName(string actionName, string entityName, string roleName)
        {
            if (!IsValidActionName(actionName))
            {
                // If the actionName is invalid, we throw an appropriate exception for the same.
                throw new DataGatewayException(
                        message: $"One of the action specified for entity:{entityName}, role:{roleName} is not valid.",
                        statusCode: System.Net.HttpStatusCode.InternalServerError,
                        subStatusCode: DataGatewayException.SubStatusCodes.ConfigValidationError);
            }
        }

        /// <summary>
        /// Returns whether the actionName is a valid
        /// - Create, Read, Update, Delete (CRUD) operation
        /// - Wildcard (*)
        /// </summary>
        /// <param name="actionName"></param>
        /// <returns>Boolean value indicating whether the actionName is valid or not.</returns>
        public static bool IsValidActionName(string actionName)
        {
            return actionName.Equals("*") || _validActions.Contains(actionName);
        }
    }
}
