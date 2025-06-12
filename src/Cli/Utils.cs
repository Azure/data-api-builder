// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Cli.Commands;
using Microsoft.Extensions.Logging;

/// <summary>
/// Contains the methods for transforming objects, serialization options.
/// </summary>
namespace Cli
{
    public class Utils
    {
        public const string PRODUCT_NAME = "Microsoft.DataApiBuilder";

        public const string WILDCARD = "*";
        public static readonly string SEPARATOR = ":";

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private static ILogger<Utils> _logger;
#pragma warning restore CS8618

        public static ILoggerFactory LoggerFactoryForCli = GetLoggerFactoryForCli();
        public static void SetCliUtilsLogger(ILogger<Utils> cliUtilsLogger)
        {
            _logger = cliUtilsLogger;
        }

        /// Creates an array of Operation element which contains one of the CRUD operation and
        /// fields to which this operation is allowed as permission setting based on the given input.
        /// </summary>
        public static EntityAction[] CreateOperations(string operations, EntityActionPolicy? policy, EntityActionFields? fields)
        {
            EntityAction[] operation_items;
            if (policy is null && fields is null)
            {
                return operations.Split(",")
                    .Select(op => EnumExtensions.Deserialize<EntityActionOperation>(op))
                    .Select(op => new EntityAction(Action: op, Fields: null, Policy: null))
                    .ToArray();
            }

            if (operations is WILDCARD)
            {
                operation_items = new[] { new EntityAction(EntityActionOperation.All, fields, policy) };
            }
            else
            {
                string[]? operation_elements = operations.Split(",");
                if (policy is not null || fields is not null)
                {
                    List<EntityAction>? operation_list = new();
                    foreach (string? operation_element in operation_elements)
                    {
                        if (EnumExtensions.TryDeserialize(operation_element, out EntityActionOperation? op))
                        {
                            EntityAction operation_item = new((EntityActionOperation)op, fields, policy);
                            operation_list.Add(operation_item);
                        }
                    }

                    operation_items = operation_list.ToArray();
                }
                else
                {
                    return operation_elements
                        .Select(op => EnumExtensions.Deserialize<EntityActionOperation>(op))
                        .Select(op => new EntityAction(Action: op, Fields: null, Policy: null))
                        .ToArray();
                }
            }

            return operation_items;
        }

        /// <summary>
        /// Given an array of operations, which is a type of JsonElement, convert it to a dictionary
        /// key: Valid operation (wild card operation will be expanded)
        /// value: Operation object
        /// </summary>
        /// <param name="operations">Array of operations which is of type JsonElement.</param>
        /// <returns>Dictionary of operations</returns>
        public static IDictionary<EntityActionOperation, EntityAction> ConvertOperationArrayToIEnumerable(EntityAction[] operations, EntitySourceType? sourceType)
        {
            Dictionary<EntityActionOperation, EntityAction> result = new();
            foreach (EntityAction operation in operations)
            {
                EntityActionOperation op = operation.Action;
                if (op is EntityActionOperation.All)
                {
                    HashSet<EntityActionOperation> resolvedOperations = sourceType is EntitySourceType.StoredProcedure ?
                        EntityAction.ValidStoredProcedurePermissionOperations :
                        EntityAction.ValidPermissionOperations;
                    // Expand wildcard to all valid operations (except execute)
                    foreach (EntityActionOperation validOp in resolvedOperations)
                    {
                        result.Add(validOp, new EntityAction(Action: validOp, Fields: null, Policy: null));
                    }
                }
                else
                {
                    result.Add(op, operation);
                }
            }

            return result;
        }

        /// <summary>
        /// Creates a single PermissionSetting Object based on role, operations, fieldsToInclude, and fieldsToExclude.
        /// </summary>
        public static EntityPermission CreatePermissions(string role, string operations, EntityActionPolicy? policy, EntityActionFields? fields)
        {
            return new(role, CreateOperations(operations, policy, fields));
        }

        /// <summary>
        /// JsonNamingPolicy to convert all the keys in Json as lower case string.
        /// </summary>
        public class LowerCaseNamingPolicy : JsonNamingPolicy
        {
            public override string ConvertName(string name) => name.ToLower();

            public static string ConvertName(Enum name) => name.ToString().ToLower();
        }

        /// <summary>
        /// Returns true on successful parsing of mappings Dictionary from IEnumerable list.
        /// Returns false in case the format of the input is not correct.
        /// </summary>
        /// <param name="mappingList">List of ':' separated values indicating exposed and backend names.</param>
        /// <param name="mappings">Output a Dictionary containing mapping from backend name to exposed name.</param>
        /// <returns> Returns true when successful else on failure, returns false. Else updated PermissionSettings array will be returned.</returns>
        public static bool TryParseMappingDictionary(IEnumerable<string> mappingList, out Dictionary<string, string> mappings)
        {
            mappings = new();
            foreach (string item in mappingList)
            {
                string[] map = item.Split(SEPARATOR);
                if (map.Length != 2)
                {
                    _logger.LogError("Invalid format for --map. Acceptable format --map \"backendName1:exposedName1,backendName2:exposedName2,...\".");
                    return false;
                }

                mappings.Add(map[0], map[1]);
            }

            return true;
        }

        /// <summary>
        /// Checks whether the URI component conforms with the expected behavior and does not contain any reserved characters.
        /// </summary>
        /// <param name="uriComponent">Path prefix/base route for REST/GraphQL APIs.</param>
        public static bool IsURIComponentValid(string? uriComponent)
        {
            // uriComponent is null only in case of cosmosDB and apiType=REST or when the runtime base-route is specified as null.
            // For these cases, validation is not required.
            if (uriComponent is null)
            {
                return true;
            }

            // removing leading '/' before checking for reserved characters.
            if (uriComponent.StartsWith('/'))
            {
                uriComponent = uriComponent.Substring(1);
            }

            return !RuntimeConfigValidatorUtil.DoesUriComponentContainReservedChars(uriComponent);
        }

        /// <summary>
        /// Returns an object of type Policy
        /// If policyRequest or policyDatabase is provided. Otherwise, returns null.
        /// </summary>
        public static EntityActionPolicy? GetPolicyForOperation(string? policyRequest, string? policyDatabase)
        {
            if (policyDatabase is null && policyRequest is null)
            {
                return null;
            }

            return new EntityActionPolicy(policyRequest, policyDatabase);
        }

        /// <summary>
        /// Returns an object of type Field
        /// If fieldsToInclude or fieldsToExclude is provided. Otherwise, returns null.
        /// </summary>
        public static EntityActionFields? GetFieldsForOperation(IEnumerable<string>? fieldsToInclude, IEnumerable<string>? fieldsToExclude)
        {
            if (fieldsToInclude is not null && fieldsToInclude.Any() || fieldsToExclude is not null && fieldsToExclude.Any())
            {
                HashSet<string>? fieldsToIncludeSet = fieldsToInclude is not null && fieldsToInclude.Any() ? new HashSet<string>(fieldsToInclude) : null;
                HashSet<string>? fieldsToExcludeSet = fieldsToExclude is not null && fieldsToExclude.Any() ? new HashSet<string>(fieldsToExclude) : new();
                return new EntityActionFields(Include: fieldsToIncludeSet, Exclude: fieldsToExcludeSet);
            }

            return null;
        }

        /// <summary>
        /// Verifies whether the operation provided by the user is valid or not
        /// Example:
        /// *, create -> Invalid
        /// create, create, read -> Invalid
        /// * -> Valid
        /// fetch, read -> Invalid
        /// read, delete -> Valid
        /// Also verifies that stored-procedures are not allowed with more than 1 CRUD operations.
        /// </summary>
        /// <param name="operations">array of string containing operations for permissions</param>
        /// <returns>True if no invalid operation is found.</returns>
        public static bool VerifyOperations(string[] operations, EntitySourceType? sourceType)
        {
            // Check if there are any duplicate operations
            // Ex: read,read,create
            HashSet<string> uniqueOperations = operations.ToHashSet();
            if (uniqueOperations.Count() != operations.Length)
            {
                _logger.LogError("Duplicate action found in --permissions");
                return false;
            }

            // Currently, Stored Procedures can be configured with only Execute Operation.
            bool isStoredProcedure = sourceType is EntitySourceType.StoredProcedure;
            if (isStoredProcedure && !VerifyExecuteOperationForStoredProcedure(operations))
            {
                return false;
            }

            bool containsWildcardOperation = false;
            foreach (string operation in uniqueOperations)
            {
                if (EnumExtensions.TryDeserialize(operation, out EntityActionOperation? op))
                {
                    if (op is EntityActionOperation.All)
                    {
                        containsWildcardOperation = true;
                    }
                    else if (!isStoredProcedure && !EntityAction.ValidPermissionOperations.Contains((EntityActionOperation)op))
                    {
                        _logger.LogError("Invalid actions found in --permissions");
                        return false;
                    }
                    else if (isStoredProcedure && !EntityAction.ValidStoredProcedurePermissionOperations.Contains((EntityActionOperation)op))
                    {
                        _logger.LogError("Invalid stored procedure action(s) found in --permissions");
                        return false;
                    }
                }
                else
                {
                    // Check for invalid operation.
                    _logger.LogError("Invalid actions found in --permissions");
                    return false;
                }
            }

            // Check for WILDCARD operation with CRUD operations.
            if (containsWildcardOperation && uniqueOperations.Count > 1)
            {
                _logger.LogError("WILDCARD(*) along with other CRUD operations in a single operation is not allowed.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// This method will parse role and operation from permission string.
        /// A valid permission string will be of the form "<<role>>:<<actions>>"
        /// It will return true if parsing is successful and add the parsed value
        /// to the out params role and operations.
        /// </summary>
        public static bool TryGetRoleAndOperationFromPermission(IEnumerable<string> permissions, [NotNullWhen(true)] out string? role, [NotNullWhen(true)] out string? operations)
        {
            // Split permission to role and operations.
            role = null;
            operations = null;
            if (permissions.Count() != 2)
            {
                _logger.LogError("Invalid format for permission. Acceptable format: --permissions \"<<role>>:<<actions>>\"");
                return false;
            }

            role = permissions.ElementAt(0);
            operations = permissions.ElementAt(1);
            return true;
        }

        /// <summary>
        /// This method will try to find the config file based on the precedence.
        /// If the config file is provided by user, it will return that.
        /// Else it will check the DAB_ENVIRONMENT variable.
        /// In case the environment variable is not set it will check for default config.
        /// If none of the files exists it will return false. Else true with output in runtimeConfigFile.
        /// In case of false, the runtimeConfigFile will be set to string.Empty.
        /// </summary>
        public static bool TryGetConfigFileBasedOnCliPrecedence(
            FileSystemRuntimeConfigLoader loader,
            string? userProvidedConfigFile,
            out string runtimeConfigFile)
        {
            if (!string.IsNullOrEmpty(userProvidedConfigFile))
            {
                /// The existence of user provided config file is not checked here.
                _logger.LogInformation("User provided config file: {userProvidedConfigFile}", userProvidedConfigFile);
                runtimeConfigFile = userProvidedConfigFile;
                return true;
            }
            else
            {
                _logger.LogInformation("Config not provided. Trying to get default config based on DAB_ENVIRONMENT...");
                _logger.LogInformation("Environment variable DAB_ENVIRONMENT is {environment}", Environment.GetEnvironmentVariable("DAB_ENVIRONMENT"));
                runtimeConfigFile = loader.GetFileNameForEnvironment(null, considerOverrides: false);
            }

            return !string.IsNullOrEmpty(runtimeConfigFile);
        }

        /// <summary>
        /// This method checks that parameter is only used with Stored Procedure, while
        /// key-fields only with table/views. Also ensures that key-fields are always
        /// provided for views.
        /// </summary>
        /// <param name="sourceType">type of the source object.</param>
        /// <param name="parameters">IEnumerable string containing parameters for stored-procedure.</param>
        /// <param name="keyFields">IEnumerable string containing key columns for table/view.</param>
        /// <returns> Returns true when successful else on failure, returns false.</returns>
        public static bool VerifyCorrectPairingOfParameterAndKeyFieldsWithType(
            EntitySourceType? sourceType,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields)
        {
            if (sourceType is EntitySourceType.StoredProcedure)
            {
                if (keyFields is not null && keyFields.Any())
                {
                    _logger.LogError("Stored Procedures don't support KeyFields.");
                    return false;
                }
            }
            else
            {
                // For Views and Tables
                if (parameters is not null && parameters.Any())
                {
                    _logger.LogError("Tables/Views don't support parameters.");
                    return false;
                }

                if (sourceType is EntitySourceType.View && (keyFields is null || !keyFields.Any()))
                {
                    _logger.LogError("Key-fields are mandatory for views, but not provided.");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Creates source object by using valid type, params, and keyfields.
        /// </summary>
        /// <param name="name">Name of the source.</param>
        /// <param name="type">Type of the source. i.e, table,view, and stored-procedure.</param>
        /// <param name="parameters">Dictionary for parameters if source is stored-procedure</param>
        /// <param name="keyFields">Array of string containing key columns for table/view type.</param>
        /// <param name="sourceObject">Outputs the created source object.
        /// It can be null, string, or DatabaseObjectSource</param>
        /// <returns>True in case of successful creation of source object.</returns>
        public static bool TryCreateSourceObject(
            string name,
            EntitySourceType? type,
            Dictionary<string, object>? parameters,
            string[]? keyFields,
            [NotNullWhen(true)] out EntitySource? sourceObject)
        {
            sourceObject = new EntitySource(
                Type: type,
                Object: name,
                Parameters: parameters,
                KeyFields: keyFields
            );

            return true;
        }

        /// <summary>
        /// This method tries to parse the source parameters Dictionary from IEnumerable list
        /// by splitting each item of the list on ':', where first item is param name and the
        /// and the second item is the value. for any other item it should fail.
        /// If Parameter List is null, no parsing happens and sourceParameter is returned as null.
        /// </summary>
        /// <param name="parametersList">List of ':' separated values indicating key and value.</param>
        /// <param name="mappings">Output a Dictionary of parameters and their values.</param>
        /// <returns> Returns true when successful else on failure, returns false.</returns>
        public static bool TryParseSourceParameterDictionary(
            IEnumerable<string>? parametersList,
            out Dictionary<string, object>? sourceParameters)
        {
            sourceParameters = null;
            if (parametersList is null)
            {
                return true;
            }

            sourceParameters = new(StringComparer.OrdinalIgnoreCase);
            foreach (string param in parametersList)
            {
                string[] items = param.Split(SEPARATOR);
                if (items.Length != 2)
                {
                    sourceParameters = null;
                    _logger.LogError("Invalid format for --source.params");
                    _logger.LogError("Correct source parameter syntax: --source.params \"key1:value1,key2:value2,...\".");
                    return false;
                }

                string paramKey = items[0];
                object paramValue = ParseStringValue(items[1]);

                sourceParameters.Add(paramKey, paramValue);
            }

            if (!sourceParameters.Any())
            {
                sourceParameters = null;
            }

            return true;
        }

        /// <summary>
        /// This method loops through every role specified for stored-procedure entity
        ///  and checks if it has only one CRUD operation.
        /// </summary>
        public static bool VerifyPermissionOperationsForStoredProcedures(
            EntityPermission[] permissionSettings)
        {
            foreach (EntityPermission permissionSetting in permissionSettings)
            {
                if (!VerifyExecuteOperationForStoredProcedure(permissionSetting.Actions))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// This method checks that stored-procedure entity
        /// is configured only with execute action
        /// </summary>
        private static bool VerifyExecuteOperationForStoredProcedure(EntityAction[] operations)
        {
            if (operations.Length > 1
                || (operations.First().Action is not EntityActionOperation.Execute && operations.First().Action is not EntityActionOperation.All))
            {
                _logger.LogError("Stored Procedure supports only execute operation.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// This method checks that stored-procedure entity
        /// is configured only with execute action
        /// </summary>
        private static bool VerifyExecuteOperationForStoredProcedure(string[] operations)
        {
            if (operations.Length > 1
                || !EnumExtensions.TryDeserialize(operations.First(), out EntityActionOperation? operation)
                || (operation is not EntityActionOperation.Execute && operation is not EntityActionOperation.All))
            {
                _logger.LogError("Stored Procedure supports only execute operation.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Check both Audience and Issuer are specified when the authentication provider is JWT.
        /// Also providing Audience or Issuer with StaticWebApps or AppService wil result in failure.
        /// </summary>
        public static bool ValidateAudienceAndIssuerForJwtProvider(
            string authenticationProvider,
            string? audience,
            string? issuer)
        {
            if (Enum.TryParse<EasyAuthType>(authenticationProvider, ignoreCase: true, out _)
                || AuthenticationOptions.SIMULATOR_AUTHENTICATION == authenticationProvider)
            {
                if (!(string.IsNullOrWhiteSpace(audience)) || !(string.IsNullOrWhiteSpace(issuer)))
                {
                    _logger.LogWarning("Audience and Issuer can't be set for EasyAuth or Simulator authentication.");
                    return true;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(audience) || string.IsNullOrWhiteSpace(issuer))
                {
                    _logger.LogError($"Authentication providers other than EasyAuth and Simulator require both Audience and Issuer.");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Converts string into either integer, double, or boolean value.
        /// If the given string is neither of the above, it returns as string.
        /// </summary>
        private static object ParseStringValue(string stringValue)
        {
            if (int.TryParse(stringValue, out int integerValue))
            {
                return integerValue;
            }
            else if (double.TryParse(stringValue, out double floatingValue))
            {
                return floatingValue;
            }
            else if (bool.TryParse(stringValue, out bool booleanValue))
            {
                return booleanValue;
            }

            return stringValue;
        }

        /// <summary>
        /// This method will write all the json string in the given file.
        /// </summary>
        public static bool WriteRuntimeConfigToFile(string file, RuntimeConfig runtimeConfig, IFileSystem fileSystem)
        {
            try
            {
                string jsonContent = runtimeConfig.ToJson();
                return WriteJsonToFile(file, jsonContent, fileSystem);
            }
            catch (Exception e)
            {
                _logger.LogError("Failed to generate the config file, operation failed with exception: {e}.", e);
                return false;
            }
        }

        public static bool WriteJsonToFile(string file, string jsonContent, IFileSystem fileSystem)
        {
            try
            {
                fileSystem.File.WriteAllText(file, jsonContent);
            }
            catch (Exception e)
            {
                _logger.LogError("Failed to generate the config file, operation failed with exception:{e}.", e);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Utility method that converts REST HTTP verb string input to RestMethod Enum.
        /// The method returns true/false corresponding to successful/unsuccessful conversion.
        /// </summary>
        /// <param name="method">String input entered by the user</param>
        /// <param name="restMethod">RestMethod Enum type</param>
        /// <returns></returns>
        public static bool TryConvertRestMethodNameToRestMethod(string? method, out SupportedHttpVerb restMethod)
        {
            if (!Enum.TryParse(method, ignoreCase: true, out restMethod))
            {
                _logger.LogError("Invalid REST Method. Supported methods are {restMethods}.", string.Join(", ", Enum.GetNames<SupportedHttpVerb>()));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Utility method that converts list of REST HTTP verbs configured for a
        /// stored procedure into an array of RestMethod Enum type.
        /// If any invalid REST methods are supplied, an empty array is returned.
        /// </summary>
        /// <param name="methods">Collection of REST HTTP verbs configured for the stored procedure</param>
        /// <returns>REST methods as an array of RestMethod Enum type.</returns>
        public static SupportedHttpVerb[] CreateRestMethods(IEnumerable<string> methods)
        {
            List<SupportedHttpVerb> restMethods = new();

            foreach (string method in methods)
            {
                SupportedHttpVerb restMethod;
                if (TryConvertRestMethodNameToRestMethod(method, out restMethod))
                {
                    restMethods.Add(restMethod);
                }
                else
                {
                    restMethods.Clear();
                    break;
                }

            }

            return restMethods.ToArray();
        }

        /// <summary>
        /// Utility method that converts the graphQL operation configured for the stored procedure to
        /// GraphQLOperation Enum type.
        /// The method returns true/false corresponding to successful/unsuccessful conversion.
        /// </summary>
        /// <param name="operation">GraphQL operation configured for the stored procedure</param>
        /// <param name="graphQLOperation">GraphQL Operation as an Enum type</param>
        /// <returns>true/false</returns>
        public static bool TryConvertGraphQLOperationNameToGraphQLOperation(string? operation, [NotNullWhen(true)] out GraphQLOperation graphQLOperation)
        {
            if (!Enum.TryParse(operation, ignoreCase: true, out graphQLOperation))
            {
                _logger.LogError(
                    "Invalid GraphQL Operation. Supported operations are {queryName} and {mutationName}.",
                    GraphQLOperation.Query,
                    GraphQLOperation.Mutation);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Method to check if the options for an entity represent a stored procedure
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        public static bool IsStoredProcedure(EntityOptions options)
        {
            if (options.SourceType is not null && EnumExtensions.TryDeserialize(options.SourceType, out EntitySourceType? sourceObjectType))
            {
                return sourceObjectType is EntitySourceType.StoredProcedure;
            }

            return false;
        }

        /// <summary>
        /// Method to determine whether the type of an entity is being converted from stored-procedure to
        /// table/view.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public static bool IsStoredProcedure(Entity entity)
        {
            return entity.Source.Type is EntitySourceType.StoredProcedure;
        }

        /// <summary>
        /// Method to determine if the type of the entity is being converted from
        /// stored-procedure to table/view.
        /// </summary>
        /// <param name="entity">Entity for which the source type conversion is being determined</param>
        /// <param name="options">Options from the CLI commands</param>
        /// <returns>True when an entity of type stored-procedure is converted to a table/view</returns>
        public static bool IsStoredProcedureConvertedToOtherTypes(Entity entity, EntityOptions options)
        {
            if (options.SourceType is null)
            {
                return false;
            }

            bool isCurrentEntityStoredProcedure = IsStoredProcedure(entity);
            bool doOptionsRepresentStoredProcedure = options.SourceType is not null && IsStoredProcedure(options);
            return isCurrentEntityStoredProcedure && !doOptionsRepresentStoredProcedure;
        }

        /// <summary>
        /// Method to determine whether the type of an entity is being changed from
        /// table/view to stored-procedure.
        /// </summary>
        /// <param name="entity">Entity for which the source type conversion is being determined</param>
        /// <param name="options">Options from the CLI commands</param>
        /// <returns>True when an entity of type table/view is converted to a stored-procedure</returns>
        public static bool IsEntityBeingConvertedToStoredProcedure(Entity entity, EntityOptions options)
        {
            if (options.SourceType is null)
            {
                return false;
            }

            bool isCurrentEntityStoredProcedure = IsStoredProcedure(entity);
            bool doOptionsRepresentStoredProcedure = options.SourceType is not null && IsStoredProcedure(options);
            return !isCurrentEntityStoredProcedure && doOptionsRepresentStoredProcedure;
        }

        /// <summary>
        /// For stored procedures, the rest HTTP verbs to be supported can be configured using
        /// --rest.methods option.
        /// Validation to ensure that configuring REST methods for a stored procedure that is
        /// not enabled for REST results in an error. This validation is run along
        /// with add command.
        /// </summary>
        /// <param name="options">Options entered using add command</param>
        /// <returns>True for invalid conflicting REST options. False when the options are valid</returns>
        public static bool CheckConflictingRestConfigurationForStoredProcedures(EntityOptions options)
        {
            return (options.RestRoute is not null && bool.TryParse(options.RestRoute, out bool restEnabled) && !restEnabled) &&
                   (options.RestMethodsForStoredProcedure is not null && options.RestMethodsForStoredProcedure.Any());
        }

        /// <summary>
        /// For stored procedures, the graphql operation to be supported can be configured using
        /// --graphql.operation.
        /// Validation to ensure that configuring GraphQL operation for a stored procedure that is
        /// not exposed for graphQL results in an error. This validation is run along with add
        /// command
        /// </summary>
        /// <param name="options"></param>
        /// <returns>True for invalid conflicting graphQL options. False when the options are not conflicting</returns>
        public static bool CheckConflictingGraphQLConfigurationForStoredProcedures(EntityOptions options)
        {
            return (options.GraphQLType is not null && bool.TryParse(options.GraphQLType, out bool graphQLEnabled) && !graphQLEnabled)
                    && (options.GraphQLOperationForStoredProcedure is not null);
        }

        /// <summary>
        /// Constructs the REST Path using the add/update command --rest option
        /// </summary>
        /// <param name="restRoute">Input entered using --rest option</param>
        /// <param name="supportedHttpVerbs">Supported HTTP verbs for the entity.</param>
        /// <param name="isCosmosDbNoSql">True when the entity is a CosmosDB NoSQL entity, and if it is true, REST is disabled.</param>
        /// <returns>Constructed REST options for the entity.</returns>
        public static EntityRestOptions ConstructRestOptions(string? restRoute, SupportedHttpVerb[]? supportedHttpVerbs, bool isCosmosDbNoSql)
        {
            // REST is not supported for CosmosDB NoSQL, so we'll forcibly disable it.
            if (isCosmosDbNoSql)
            {
                return new(Enabled: false);
            }

            EntityRestOptions restOptions = new(supportedHttpVerbs);

            // Default state for REST is enabled, so if no value is provided, we enable it
            if (restRoute is null)
            {
                return restOptions with { Enabled = true, Methods = supportedHttpVerbs };
            }
            else
            {
                if (bool.TryParse(restRoute, out bool restEnabled))
                {
                    restOptions = restOptions with { Enabled = restEnabled };
                }
                else
                {
                    restOptions = restOptions with { Enabled = true, Path = "/" + restRoute };
                }
            }

            return restOptions;
        }

        /// <summary>
        /// Constructs the graphQL Type from add/update command --graphql option
        /// </summary>
        /// <param name="graphQL">GraphQL type input from the CLI commands</param>
        /// <param name="graphQLOperationsForStoredProcedures">GraphQL operation input from the CLI commands.</param>
        /// <returns>Constructed GraphQL Type</returns>
        public static EntityGraphQLOptions ConstructGraphQLTypeDetails(string? graphQL, GraphQLOperation? graphQLOperationsForStoredProcedures)
        {
            EntityGraphQLOptions graphQLType = new(
                Singular: string.Empty,
                Plural: string.Empty,
                Operation: graphQLOperationsForStoredProcedures);

            // Default state for GraphQL is enabled, so if no value is provided, we enable it
            if (graphQL is null)
            {
                return graphQLType with { Enabled = true };
            }
            else
            {
                if (bool.TryParse(graphQL, out bool graphQLEnabled))
                {
                    graphQLType = graphQLType with { Enabled = graphQLEnabled };
                }
                else
                {
                    string singular, plural = string.Empty;
                    if (graphQL.Contains(SEPARATOR))
                    {
                        string[] arr = graphQL.Split(SEPARATOR);
                        if (arr.Length != 2)
                        {
                            _logger.LogError("Invalid format for --graphql. Accepted values are true/false, a string, or a pair of string in the format <singular>:<plural>");
                            return graphQLType;
                        }

                        singular = arr[0];
                        plural = arr[1];
                    }
                    else
                    {
                        singular = graphQL;
                    }

                    // If we have singular/plural text we infer that GraphQL is enabled
                    graphQLType = graphQLType with { Enabled = true, Singular = singular, Plural = plural };
                }
            }

            return graphQLType;
        }

        /// <summary>
        /// Constructs the EntityCacheOption for Add/Update.
        /// </summary>
        /// <param name="cacheEnabled">String value that defines if the cache is enabled.</param>
        /// <param name="cacheTtl">Int that gives time to live in seconds for cache.</param>
        /// <returns>EntityCacheOption if values are provided for cacheEnabled or cacheTtl, null otherwise.</returns>
        public static EntityCacheOptions? ConstructCacheOptions(string? cacheEnabled, string? cacheTtl)
        {
            if (cacheEnabled is null && cacheTtl is null)
            {
                return null;
            }

            EntityCacheOptions cacheOptions = new();
            bool isEnabled = false;
            bool isCacheTtlUserProvided = false;
            int ttl = EntityCacheOptions.DEFAULT_TTL_SECONDS;

            if (cacheEnabled is not null && !bool.TryParse(cacheEnabled, out isEnabled))
            {
                _logger.LogError("Invalid format for --cache.enabled. Accepted values are true/false.");
            }

            if ((cacheTtl is not null && !int.TryParse(cacheTtl, out ttl)) || ttl < 0)
            {
                _logger.LogError("Invalid format for --cache.ttl. Accepted values are any non-negative integer.");
            }

            // This is needed so the cacheTtl is correctly written to config.
            if (cacheTtl is not null)
            {
                isCacheTtlUserProvided = true;
            }

            // Both cacheEnabled and cacheTtl can not be null here, so if either one
            // is, the other is not, and we return the cacheOptions with just that other
            // value.
            if (cacheEnabled is null)
            {
                return cacheOptions with { TtlSeconds = ttl, UserProvidedTtlOptions = isCacheTtlUserProvided };
            }

            if (cacheTtl is null)
            {
                return cacheOptions with { Enabled = isEnabled };
            }

            return cacheOptions with { Enabled = isEnabled, TtlSeconds = ttl, UserProvidedTtlOptions = isCacheTtlUserProvided };
        }

        /// <summary>
        /// Check if add/update command has Entity provided. Return false otherwise.
        /// </summary>
        public static bool IsEntityProvided(string? entity, ILogger cliLogger, string command)
        {
            if (string.IsNullOrWhiteSpace(entity))
            {
                cliLogger.LogError("Entity name is missing. Usage: dab {command} [entity-name] [{command}-options]", command, command);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns ILoggerFactory with CLI custom logger provider.
        /// </summary>
        public static ILoggerFactory GetLoggerFactoryForCli()
        {
            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new CustomLoggerProvider());
            return loggerFactory;
        }
    }
}
