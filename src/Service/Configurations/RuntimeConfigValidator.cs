using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Microsoft.Extensions.Logging;
using PermissionOperation = Azure.DataApiBuilder.Config.PermissionOperation;

namespace Azure.DataApiBuilder.Service.Configurations
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

        // actionKey is the key used in json runtime config to
        // specify the action name.
        private static readonly string _actionKey = "action";

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
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE,
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            if (runtimeConfig.DatabaseType is DatabaseType.cosmos)
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

            if (runtimeConfig.GraphQLGlobalSettings.Enabled)
            {
                ValidateEntityNamesInConfig(runtimeConfig.Entities);
            }
        }

        /// <summary>
        /// Check whether the entity name defined in runtime config only contains
        /// characters allowed for GraphQL names.
        /// Does not perform validation for entities which do not
        /// have GraphQL configuration: when entity.GraphQL == false or null.
        /// </summary>
        /// <seealso cref="https://spec.graphql.org/October2021/#Name"/>
        /// <param name="runtimeConfig"></param>
        public static void ValidateEntityNamesInConfig(Dictionary<string, Entity> entityCollection)
        {
            foreach (string entityName in entityCollection.Keys)
            {
                Entity entity = entityCollection[entityName];

                if (entity.GraphQL is null)
                {
                    continue;
                }
                else if (entity.GraphQL is bool graphQLEnabled)
                {
                    if (!graphQLEnabled)
                    {
                        continue;
                    }

                    ValidateNameRequirements(entityName);
                }
                else if (entity.GraphQL is GraphQLEntitySettings graphQLSettings)
                {
                    if (graphQLSettings.Type is string graphQLName)
                    {
                        ValidateNameRequirements(graphQLName);
                    }
                    else if (graphQLSettings.Type is SingularPlural singularPluralSettings)
                    {
                        ValidateNameRequirements(singularPluralSettings.Singular);

                        if (singularPluralSettings.Plural is not null)
                        {
                            ValidateNameRequirements(singularPluralSettings.Plural);
                        }
                    }
                }
            }
        }

        private static void ValidateNameRequirements(string entityName)
        {
            if (GraphQLNaming.ViolatesNamePrefixRequirements(entityName) ||
                GraphQLNaming.ViolatesNameRequirements(entityName))
            {
                throw new DataApiBuilderException(
                    message: $"Entity {entityName} contains characters disallowed by GraphQL.",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
            }
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
        /// <exception cref="DataApiBuilderException">Throws exception whenever some validation fails.</exception>
        public void ValidatePermissionsInConfig(RuntimeConfig runtimeConfig)
        {
            foreach ((string entityName, Entity entity) in runtimeConfig.Entities)
            {
                foreach (PermissionSetting permissionSetting in entity.Permissions)
                {
                    string roleName = permissionSetting.Role;
                    Object[] actions = permissionSetting.Operations;
                    foreach (Object action in actions)
                    {
                        if (action is null)
                        {
                            throw GetInvalidActionException(entityName, roleName, actionName: "null");
                        }

                        // Evaluate actionOp as the current operation to be validated.
                        Operation actionOp;
                        if (((JsonElement)action!).ValueKind is JsonValueKind.String)
                        {
                            string actionName = action.ToString()!;
                            if (AuthorizationResolver.WILDCARD.Equals(actionName))
                            {
                                actionOp = Operation.All;
                            }
                            else if (!Enum.TryParse<Operation>(actionName, ignoreCase: true, out actionOp) ||
                                !IsValidPermissionAction(actionOp))
                            {
                                throw GetInvalidActionException(entityName, roleName, actionName);
                            }
                        }
                        else
                        {
                            PermissionOperation configAction;
                            try
                            {
                                configAction = JsonSerializer.Deserialize<Config.PermissionOperation>(action.ToString()!)!;

                            }
                            catch
                            {
                                throw new DataApiBuilderException(
                                    message: $"One of the action specified for entity:{entityName} is not well formed.",
                                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                            }

                            actionOp = configAction.Name;
                            // If we have reached this point, it means that we don't have any invalid
                            // data type in actions. However we need to ensure that the actionOp is valid.
                            if (!IsValidPermissionAction(actionOp))
                            {
                                bool isActionPresent = ((JsonElement)action).TryGetProperty(_actionKey,
                                    out JsonElement actionElement);
                                if (!isActionPresent)
                                {
                                    throw new DataApiBuilderException(
                                        message: $"action cannot be omitted for entity: {entityName}, role:{roleName}",
                                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                                }

                                throw GetInvalidActionException(entityName, roleName, actionElement.ToString());
                            }

                            if (configAction.Fields is not null)
                            {
                                // Check if the IncludeSet/ExcludeSet contain wildcard. If they contain wildcard, we make sure that they
                                // don't contain any other field. If they do, we throw an appropriate exception.
                                if (configAction.Fields.Include.Contains(AuthorizationResolver.WILDCARD) && configAction.Fields.Include.Count > 1 ||
                                    configAction.Fields.Exclude.Contains(AuthorizationResolver.WILDCARD) && configAction.Fields.Exclude.Count > 1)
                                {
                                    // See if included or excluded columns contain wildcard and another field.
                                    // If thats the case with both of them, we specify 'included' in error.
                                    string misconfiguredColumnSet = configAction.Fields.Include.Contains(AuthorizationResolver.WILDCARD)
                                        && configAction.Fields.Include.Count > 1 ? "included" : "excluded";
                                    string actionName = actionOp is Operation.All ? "*" : actionOp.ToString();
                                    throw new DataApiBuilderException(
                                            message: $"No other field can be present with wildcard in the {misconfiguredColumnSet} set for:" +
                                            $" entity:{entityName}, role:{permissionSetting.Role}, action:{actionName}",
                                            statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                                            subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
                                }

                                if (configAction.Policy is not null && configAction.Policy.Database is not null)
                                {
                                    // validate that all the fields mentioned in database policy are accessible to user.
                                    AreFieldsAccessible(configAction.Policy.Database,
                                        configAction.Fields.Include, configAction.Fields.Exclude);

                                    // validate that all the claimTypes in the policy are well formed.
                                    ValidateClaimsInPolicy(configAction.Policy.Database);
                                }
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
                    Object[] actions = permissionSetting.Operations;

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
                            PermissionOperation configAction;
                            configAction = JsonSerializer.Deserialize<Config.PermissionOperation>(action.ToString()!)!;

                            if (configAction.Policy is not null && configAction.Policy.Database is not null)
                            {
                                // Remove all the occurences of @item. directive from the policy.
                                configAction.Policy.Database = ProcessFieldsInPolicy(configAction.Policy.Database);
                            }

                            processedActions.Add(JsonSerializer.SerializeToElement(configAction));
                        }
                    }

                    // Update the permissionsetting.Actions to point to the processedActions.
                    permissionSetting.Operations = processedActions.ToArray();
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
        /// Method to do different validations on claims in the policy.
        /// </summary>
        /// <param name="policy">The policy to be validated and processed.</param>
        /// <returns>Processed policy</returns>
        /// <exception cref="DataApiBuilderException">Throws exception when one or the other validations fail.</exception>
        private static void ValidateClaimsInPolicy(string policy)
        {
            // Find all the claimTypes from the policy
            MatchCollection claimTypes = GetClaimTypesInPolicy(policy);

            foreach (Match claimType in claimTypes)
            {
                // Remove the prefix @claims. from the claimType
                string typeOfClaim = claimType.Value.Substring(AuthorizationResolver.CLAIM_PREFIX.Length);

                if (string.IsNullOrWhiteSpace(typeOfClaim))
                {
                    // Empty claimType is not allowed
                    throw new DataApiBuilderException(
                        message: $"Claimtype cannot be empty.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError
                        );
                }

                if (_invalidClaimCharsRgx.IsMatch(typeOfClaim))
                {
                    // Not a valid claimType containing allowed characters
                    throw new DataApiBuilderException(
                        message: $"Invalid format for claim type {typeOfClaim} supplied in policy.",
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError
                        );
                }
            } // MatchType claimType
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
                    throw new DataApiBuilderException(
                    message: $"Not all the columns required by policy are accessible.",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
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
        /// <exception cref="DataApiBuilderException">Throws exception if the field is not accessible.</exception>
        private static bool IsFieldAccessible(Match columnNameMatch, HashSet<string> includedFields, HashSet<string> excludedFields)
        {
            string columnName = columnNameMatch.Value.Substring(AuthorizationResolver.FIELD_PREFIX.Length);
            if (excludedFields.Contains(columnName!) || excludedFields.Contains(AuthorizationResolver.WILDCARD) ||
                !(includedFields.Contains(AuthorizationResolver.WILDCARD) || includedFields.Contains(columnName)))
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
        /// Helper function to create a DataApiBuilderException object.
        /// Called when the actionName is not valid.
        /// </summary>
        /// <param name="entityName">Name of entity for which invalid action is supplied.</param>
        /// <param name="roleName">Name of role for which invalid action is supplied.</param>
        /// <param name="actionName">Name of invalid action.</param>
        /// <returns></returns>
        private static DataApiBuilderException GetInvalidActionException(string entityName, string roleName, string actionName)
        {
            return new DataApiBuilderException(
                message: $"action:{actionName} specified for entity:{entityName}, role:{roleName} is not valid.",
                statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
        }

        /// <summary>
        /// Returns whether the action is a valid
        /// - Create, Read, Update, Delete (CRUD) operation
        /// - All (*)
        /// </summary>
        /// <param name="action"></param>
        /// <returns>Boolean value indicating whether the action is valid or not.</returns>
        public static bool IsValidPermissionAction(Operation action)
        {
            return action is Operation.All || PermissionOperation.ValidPermissionOperations.Contains(action);
        }
    }
}
