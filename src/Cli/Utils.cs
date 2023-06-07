// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Humanizer;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Config.AuthenticationConfig;
using static Azure.DataApiBuilder.Config.MergeJsonProvider;
using static Azure.DataApiBuilder.Config.RuntimeConfigPath;
using static Azure.DataApiBuilder.Service.Configurations.RuntimeConfigProvider;
using static Azure.DataApiBuilder.Service.Configurations.RuntimeConfigValidator;
using PermissionOperation = Azure.DataApiBuilder.Config.PermissionOperation;

/// <summary>
/// Contains the methods for transforming objects, serialization options.
/// </summary>
namespace Cli
{
    public class Utils
    {
        public const string WILDCARD = "*";
        public static readonly string SEPARATOR = ":";

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private static ILogger<Utils> _logger;
#pragma warning restore CS8618

        public static void SetCliUtilsLogger(ILogger<Utils> cliUtilsLogger)
        {
            _logger = cliUtilsLogger;
        }

        /// <summary>
        /// Creates the REST object which can be either a boolean value
        /// or a RestEntitySettings object containing api route based on the input.
        /// Returns null when no REST configuration is provided.
        /// </summary>
        public static object? GetRestDetails(object? restDetail = null, RestMethod[]? restMethods = null)
        {
            if (restDetail is null && restMethods is null)
            {
                return null;
            }
            // Tables, Views and Stored Procedures that are enabled for REST without custom
            // path or methods.
            else if (restDetail is not null && restMethods is null)
            {
                if (restDetail is true || restDetail is false)
                {
                    return restDetail;
                }
                else
                {
                    return new RestEntitySettings(Path: restDetail);
                }
            }
            //Stored Procedures that have REST methods defined without a custom REST path definition
            else if (restMethods is not null && restDetail is null)
            {
                return new RestStoredProcedureEntitySettings(RestMethods: restMethods);
            }

            //Stored Procedures that have custom REST path and methods defined 
            return new RestStoredProcedureEntityVerboseSettings(Path: restDetail, RestMethods: restMethods!);
        }

        /// <summary>
        /// Creates the graphql object which can be either a boolean value
        /// or a GraphQLEntitySettings object containing graphql type {singular, plural} based on the input
        /// </summary>
        public static object? GetGraphQLDetails(object? graphQLDetail, GraphQLOperation? graphQLOperation = null)
        {

            if (graphQLDetail is null && graphQLOperation is null)
            {
                return null;
            }
            // Tables, view or stored procedures that are either enabled for graphQL without custom operation
            // definitions and with/without a custom graphQL type definition.
            else if (graphQLDetail is not null && graphQLOperation is null)
            {
                if (graphQLDetail is bool graphQLEnabled)
                {
                    return graphQLEnabled;
                }
                else
                {
                    return new GraphQLEntitySettings(Type: graphQLDetail);
                }
            }
            // Stored procedures that are defined with custom graphQL operations but without
            // custom type definitions.
            else if (graphQLDetail is null && graphQLOperation is not null)
            {
                return new GraphQLStoredProcedureEntityOperationSettings(GraphQLOperation: graphQLOperation.ToString()!.ToLower());
            }

            // Stored procedures that are defined with custom graphQL type definition and
            // custom a graphQL operation.
            return new GraphQLStoredProcedureEntityVerboseSettings(Type: graphQLDetail, GraphQLOperation: graphQLOperation.ToString()!.ToLower());

        }

        /// <summary>
        /// Try convert operation string to Operation Enum.
        /// </summary>
        /// <param name="operationName">operation string.</param>
        /// <param name="operation">Operation Enum output.</param>
        /// <returns>True if convert is successful. False otherwise.</returns>
        public static bool TryConvertOperationNameToOperation(string? operationName, out Operation operation)
        {
            if (!Enum.TryParse(operationName, ignoreCase: true, out operation))
            {
                if (operationName is not null && operationName.Equals(WILDCARD, StringComparison.OrdinalIgnoreCase))
                {
                    operation = Operation.All;
                }
                else
                {
                    _logger.LogError($"Invalid operation Name: {operationName}.");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Creates an array of Operation element which contains one of the CRUD operation and
        /// fields to which this operation is allowed as permission setting based on the given input.
        /// </summary>
        public static object[] CreateOperations(string operations, Policy? policy, Field? fields)
        {
            object[] operation_items;
            if (policy is null && fields is null)
            {
                return operations.Split(",");
            }

            if (operations is WILDCARD)
            {
                operation_items = new object[] { new PermissionOperation(Operation.All, policy, fields) };
            }
            else
            {
                string[]? operation_elements = operations.Split(",");
                if (policy is not null || fields is not null)
                {
                    List<object>? operation_list = new();
                    foreach (string? operation_element in operation_elements)
                    {
                        if (TryConvertOperationNameToOperation(operation_element, out Operation op))
                        {
                            PermissionOperation? operation_item = new(op, policy, fields);
                            operation_list.Add(operation_item);
                        }
                    }

                    operation_items = operation_list.ToArray();
                }
                else
                {
                    operation_items = operation_elements;
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
        public static IDictionary<Operation, PermissionOperation> ConvertOperationArrayToIEnumerable(object[] operations, SourceType sourceType)
        {
            Dictionary<Operation, PermissionOperation> result = new();
            foreach (object operation in operations)
            {
                JsonElement operationJson = (JsonElement)operation;
                if (operationJson.ValueKind is JsonValueKind.String)
                {
                    if (TryConvertOperationNameToOperation(operationJson.GetString(), out Operation op))
                    {
                        if (op is Operation.All)
                        {
                            HashSet<Operation> resolvedOperations = sourceType is SourceType.StoredProcedure ? PermissionOperation.ValidStoredProcedurePermissionOperations : PermissionOperation.ValidPermissionOperations;
                            // Expand wildcard to all valid operations (except execute)
                            foreach (Operation validOp in resolvedOperations)
                            {
                                result.Add(validOp, new PermissionOperation(validOp, null, null));
                            }
                        }
                        else
                        {
                            result.Add(op, new PermissionOperation(op, null, null));
                        }
                    }
                }
                else
                {
                    PermissionOperation ac = operationJson.Deserialize<PermissionOperation>(GetSerializationOptions())!;

                    if (ac.Name is Operation.All)
                    {
                        // Expand wildcard to all valid operations except execute.
                        HashSet<Operation> resolvedOperations = sourceType is SourceType.StoredProcedure ? PermissionOperation.ValidStoredProcedurePermissionOperations : PermissionOperation.ValidPermissionOperations;
                        foreach (Operation validOp in resolvedOperations)
                        {
                            result.Add(validOp, new PermissionOperation(validOp, Policy: ac.Policy, Fields: ac.Fields));
                        }
                    }
                    else
                    {
                        result.Add(ac.Name, ac);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Creates a single PermissionSetting Object based on role, operations, fieldsToInclude, and fieldsToExclude.
        /// </summary>
        public static PermissionSetting CreatePermissions(string role, string operations, Policy? policy, Field? fields)
        {
            return new PermissionSetting(role, CreateOperations(operations, policy, fields));
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
        /// Returns the Serialization option used to convert objects into JSON.
        /// Not escaping any special unicode characters.
        /// Ignoring properties with null values.
        /// Keeping all the keys in lowercase.
        /// </summary>
        public static JsonSerializerOptions GetSerializationOptions()
        {
            JsonSerializerOptions? options = new()
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = new LowerCaseNamingPolicy(),
                // As of .NET Core 7, JsonDocument and JsonSerializer only support skipping or disallowing 
                // of comments; they do not support loading them. If we set JsonCommentHandling.Allow for either,
                // it will throw an exception.
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: new LowerCaseNamingPolicy()));
            return options;
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
                    _logger.LogError("Invalid format for --map. " +
                        "Acceptable format --map \"backendName1:exposedName1,backendName2:exposedName2,...\".");
                    return false;
                }

                mappings.Add(map[0], map[1]);
            }

            return true;
        }

        /// <summary>
        /// Returns the default global settings.
        /// </summary>
        public static Dictionary<GlobalSettingsType, object> GetDefaultGlobalSettings(HostModeType hostMode,
                                                                                      IEnumerable<string>? corsOrigin,
                                                                                      string authenticationProvider,
                                                                                      string? audience = null,
                                                                                      string? issuer = null,
                                                                                      string? restPath = GlobalSettings.REST_DEFAULT_PATH,
                                                                                      bool restEnabled = true,
                                                                                      string graphqlPath = GlobalSettings.GRAPHQL_DEFAULT_PATH,
                                                                                      bool graphqlEnabled = true)
        {
            // Prefix rest path with '/', if not already present.
            if (restPath is not null && !restPath.StartsWith('/'))
            {
                restPath = "/" + restPath;
            }

            // Prefix graphql path with '/', if not already present.
            if (!graphqlPath.StartsWith('/'))
            {
                graphqlPath = "/" + graphqlPath;
            }

            Dictionary<GlobalSettingsType, object> defaultGlobalSettings = new();

            // If restPath is null, it implies we are dealing with cosmosdb_nosql,
            // which only supports graphql.
            if (restPath is not null)
            {
                defaultGlobalSettings.Add(GlobalSettingsType.Rest, new RestGlobalSettings(Enabled: restEnabled, Path: restPath));
            }

            defaultGlobalSettings.Add(GlobalSettingsType.GraphQL, new GraphQLGlobalSettings(Enabled: graphqlEnabled, Path: graphqlPath));
            defaultGlobalSettings.Add(
                GlobalSettingsType.Host,
                GetDefaultHostGlobalSettings(
                    hostMode,
                    corsOrigin,
                    authenticationProvider,
                    audience,
                    issuer));
            return defaultGlobalSettings;
        }

        /// <summary>
        /// Returns true if the api path contains any reserved characters like "[\.:\?#/\[\]@!$&'()\*\+,;=]+"
        /// </summary>
        /// <param name="apiPath">path prefix for rest/graphql apis</param>
        /// <param name="apiType">Either REST or GraphQL</param>
        public static bool IsApiPathValid(string? apiPath, ApiType apiType)
        {
            // apiPath is null only in case of cosmosDB and apiType=REST. For this case, validation is not required.
            // Since, cosmosDB do not support REST calls.
            if (apiPath is null)
            {
                return true;
            }

            // removing leading '/' before checking for forbidden characters.
            if (apiPath.StartsWith('/'))
            {
                apiPath = apiPath.Substring(1);
            }

            try
            {
                DoApiPathInvalidCharCheck(apiPath, apiType);
                return true;
            }
            catch (DataApiBuilderException ex)
            {
                _logger.LogError(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Returns the default host Global Settings
        /// If the user doesn't specify host mode. Default value to be used is Production.
        /// Sample:
        // "host": {
        //     "mode": "production",
        //     "cors": {
        //         "origins": [],
        //         "allow-credentials": true
        //     },
        //     "authentication": {
        //         "provider": "StaticWebApps"
        //     }
        // }
        /// </summary>
        public static HostGlobalSettings GetDefaultHostGlobalSettings(
            HostModeType hostMode,
            IEnumerable<string>? corsOrigin,
            string authenticationProvider,
            string? audience,
            string? issuer)
        {
            string[]? corsOriginArray = corsOrigin is null ? new string[] { } : corsOrigin.ToArray();
            Cors cors = new(Origins: corsOriginArray);
            AuthenticationConfig authenticationConfig;
            if (Enum.TryParse<EasyAuthType>(authenticationProvider, ignoreCase: true, out _)
                || SIMULATOR_AUTHENTICATION.Equals(authenticationProvider))
            {
                authenticationConfig = new(Provider: authenticationProvider);
            }
            else
            {
                authenticationConfig = new(
                    Provider: authenticationProvider,
                    Jwt: new(audience, issuer)
                );
            }

            return new HostGlobalSettings(
                Mode: hostMode,
                Cors: cors,
                Authentication: authenticationConfig);
        }

        /// <summary>
        /// Returns an object of type Policy
        /// If policyRequest or policyDatabase is provided. Otherwise, returns null.
        /// </summary>
        public static Policy? GetPolicyForOperation(string? policyRequest, string? policyDatabase)
        {
            if (policyRequest is not null || policyDatabase is not null)
            {
                return new Policy(policyRequest, policyDatabase);
            }

            return null;
        }

        /// <summary>
        /// Returns an object of type Field
        /// If fieldsToInclude or fieldsToExclude is provided. Otherwise, returns null.
        /// </summary>
        public static Field? GetFieldsForOperation(IEnumerable<string>? fieldsToInclude, IEnumerable<string>? fieldsToExclude)
        {
            if (fieldsToInclude is not null && fieldsToInclude.Any() || fieldsToExclude is not null && fieldsToExclude.Any())
            {
                HashSet<string>? fieldsToIncludeSet = fieldsToInclude is not null && fieldsToInclude.Any() ? new HashSet<string>(fieldsToInclude) : null;
                HashSet<string>? fieldsToExcludeSet = fieldsToExclude is not null && fieldsToExclude.Any() ? new HashSet<string>(fieldsToExclude) : null;
                return new Field(fieldsToIncludeSet, fieldsToExcludeSet);
            }

            return null;
        }

        /// <summary>
        /// Try to read and deserialize runtime config from a file.
        /// </summary>
        /// <param name="file">File path.</param>
        /// <param name="runtimeConfigJson">Runtime config output. On failure, this will be null.</param>
        /// <returns>True on success. On failure, return false and runtimeConfig will be set to null.</returns>
        public static bool TryReadRuntimeConfig(string file, out string runtimeConfigJson)
        {
            runtimeConfigJson = string.Empty;

            if (!File.Exists(file))
            {
                _logger.LogError($"Couldn't find config  file: {file}. " +
                    "Please run: dab init <options> to create a new config file.");
                return false;
            }

            // Read existing config file content.
            //
            runtimeConfigJson = File.ReadAllText(file);
            return true;
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
        public static bool VerifyOperations(string[] operations, SourceType sourceType)
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
            bool isStoredProcedure = sourceType is SourceType.StoredProcedure;
            if (isStoredProcedure && !VerifyExecuteOperationForStoredProcedure(operations))
            {
                return false;
            }

            bool containsWildcardOperation = false;
            foreach (string operation in uniqueOperations)
            {
                if (TryConvertOperationNameToOperation(operation, out Operation op))
                {
                    if (op is Operation.All)
                    {
                        containsWildcardOperation = true;
                    }
                    else if (!isStoredProcedure && !PermissionOperation.ValidPermissionOperations.Contains(op))
                    {
                        _logger.LogError("Invalid actions found in --permissions");
                        return false;
                    }
                    else if (isStoredProcedure && !PermissionOperation.ValidStoredProcedurePermissionOperations.Contains(op))
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
            if (containsWildcardOperation && uniqueOperations.Count() > 1)
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
        public static bool TryGetRoleAndOperationFromPermission(IEnumerable<string> permissions, out string? role, out string? operations)
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
            string? userProvidedConfigFile,
            out string runtimeConfigFile)
        {
            if (!string.IsNullOrEmpty(userProvidedConfigFile))
            {
                /// The existence of user provided config file is not checked here.
                _logger.LogInformation($"User provided config file: {userProvidedConfigFile}");
                RuntimeConfigPath.CheckPrecedenceForConfigInEngine = false;
                runtimeConfigFile = userProvidedConfigFile;
                return true;
            }
            else
            {
                _logger.LogInformation("Config not provided. Trying to get default config based on DAB_ENVIRONMENT...");
                _logger.LogInformation("Environment variable DAB_ENVIRONMENT is {value}", Environment.GetEnvironmentVariable("DAB_ENVIRONMENT"));
                /// Need to reset to true explicitly so any that any re-invocations of this function
                /// get simulated as being called for the first time specifically useful for tests.
                RuntimeConfigPath.CheckPrecedenceForConfigInEngine = true;
                runtimeConfigFile = RuntimeConfigPath.GetFileNameForEnvironment(
                        hostingEnvironmentName: null,
                        considerOverrides: false);

                /// So that the check doesn't run again when starting engine
                RuntimeConfigPath.CheckPrecedenceForConfigInEngine = false;
            }

            return !string.IsNullOrEmpty(runtimeConfigFile);
        }

        /// <summary>
        /// Checks if config can be correctly resolved and parsed by deserializing the
        /// json config into runtime config object.
        /// Also checks that connection-string is not null or empty whitespace.
        /// If parsing is successful and the config has valid connection-string, it
        /// returns true with out as deserializedConfig, else returns false.
        /// </summary>
        public static bool CanParseConfigCorrectly(
            string configFile,
            [NotNullWhen(true)] out RuntimeConfig? deserializedRuntimeConfig)
        {
            deserializedRuntimeConfig = null;
            string? runtimeConfigJson;

            try
            {
                // Tries to read the config and resolve environment variables.
                runtimeConfigJson = GetRuntimeConfigJsonString(configFile);
            }
            catch (Exception e)
            {
                _logger.LogError("Failed due to: {exceptionMessage}", e.Message);
                return false;
            }

            if (string.IsNullOrEmpty(runtimeConfigJson) || !RuntimeConfig.TryGetDeserializedRuntimeConfig(
                    runtimeConfigJson,
                    out deserializedRuntimeConfig,
                    logger: null))
            {
                _logger.LogError("Failed to parse the config file: {configFile}.", configFile);
                return false;
            }

            if (string.IsNullOrWhiteSpace(deserializedRuntimeConfig.ConnectionString))
            {
                _logger.LogError($"Invalid connection-string provided in the config.");
                return false;
            }

            return true;
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
            SourceType sourceType,
            IEnumerable<string>? parameters,
            IEnumerable<string>? keyFields)
        {
            if (sourceType is SourceType.StoredProcedure)
            {
                if (keyFields is not null && keyFields.Any())
                {
                    _logger.LogError("Stored Procedures don't support keyfields.");
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

                // For Views
                if (sourceType is SourceType.View && (keyFields is null || !keyFields.Any()))
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
            SourceType type,
            Dictionary<string, object>? parameters,
            string[]? keyFields,
            [NotNullWhen(true)] out object? sourceObject)
        {

            // If type is Table along with that parameter and keyfields is null then return the source as string.
            if (SourceType.Table.Equals(type) && parameters is null && keyFields is null)
            {
                sourceObject = name;
                return true;
            }

            sourceObject = new DatabaseObjectSource(
                Type: type,
                Name: name,
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
            PermissionSetting[] permissionSettings)
        {
            foreach (PermissionSetting permissionSetting in permissionSettings)
            {
                if (!VerifyExecuteOperationForStoredProcedure(permissionSetting.Operations))
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
        private static bool VerifyExecuteOperationForStoredProcedure(object[] operations)
        {
            if (operations.Length > 1
                || !TryGetOperationName(operations.First(), out Operation operationName)
                || (operationName is not Operation.Execute && operationName is not Operation.All))
            {
                _logger.LogError("Stored Procedure supports only execute operation.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if the operation is string or PermissionOperation object
        /// and tries to parse the operation name accordingly.
        /// Returns true on successful parsing.
        /// </summary>
        public static bool TryGetOperationName(object operation, out Operation operationName)
        {
            JsonElement operationJson = JsonSerializer.SerializeToElement(operation);
            if (operationJson.ValueKind is JsonValueKind.String)
            {
                return TryConvertOperationNameToOperation(operationJson.GetString(), out operationName);
            }

            PermissionOperation? action = JsonSerializer.Deserialize<PermissionOperation>(operationJson);
            if (action is null)
            {
                _logger.LogError($"Failed to parse the operation: {operation}.");
                operationName = Operation.None;
                return false;
            }

            operationName = action.Name;
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
                || SIMULATOR_AUTHENTICATION.Equals(authenticationProvider))
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
        public static bool WriteJsonContentToFile(string file, string jsonContent)
        {
            try
            {
                File.WriteAllText(file, jsonContent);
            }
            catch (Exception e)
            {
                _logger.LogError($"Failed to generate the config file, operation failed with exception:{e}.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// This method will check if DAB_ENVIRONMENT value is set.
        /// If yes, it will try to merge dab-config.json with dab-config.{DAB_ENVIRONMENT}.json
        /// and create a merged file called dab-config.{DAB_ENVIRONMENT}.merged.json
        /// </summary>
        /// <returns>Returns the name of the merged config if successful.</returns>
        public static bool TryMergeConfigsIfAvailable([NotNullWhen(true)] out string? mergedConfigFile)
        {
            string? environmentValue = Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME);
            mergedConfigFile = null;
            if (!string.IsNullOrEmpty(environmentValue))
            {
                string baseConfigFile = RuntimeConfigPath.DefaultName;
                string environmentBasedConfigFile = RuntimeConfigPath.GetFileName(environmentValue, considerOverrides: false);

                if (DoesFileExistInCurrentDirectory(baseConfigFile) && !string.IsNullOrEmpty(environmentBasedConfigFile))
                {
                    try
                    {
                        string baseConfigJson = File.ReadAllText(baseConfigFile);
                        string overrideConfigJson = File.ReadAllText(environmentBasedConfigFile);
                        string currentDir = Directory.GetCurrentDirectory();
                        _logger.LogInformation("Using DAB_ENVIRONMENT = {value}", environmentValue);
                        _logger.LogInformation($"Merging {Path.Combine(currentDir, baseConfigFile)}"
                            + $" and {Path.Combine(currentDir, environmentBasedConfigFile)}");
                        string mergedConfigJson = Merge(baseConfigJson, overrideConfigJson);
                        mergedConfigFile = RuntimeConfigPath.GetMergedFileNameForEnvironment(CONFIGFILE_NAME, environmentValue);
                        File.WriteAllText(mergedConfigFile, mergedConfigJson);
                        _logger.LogInformation($"Generated merged config file: {Path.Combine(currentDir, mergedConfigFile)}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to merge the config files.");
                        mergedConfigFile = null;
                        return false;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Utility method that converts REST HTTP verb string input to RestMethod Enum.
        /// The method returns true/false corresponding to successful/unsuccessful conversion.
        /// </summary>
        /// <param name="method">String input entered by the user</param>
        /// <param name="restMethod">RestMethod Enum type</param>
        /// <returns></returns>
        public static bool TryConvertRestMethodNameToRestMethod(string? method, out RestMethod restMethod)
        {
            if (!Enum.TryParse(method, ignoreCase: true, out restMethod))
            {
                _logger.LogError($"Invalid REST Method. Supported methods are {RestMethod.Get.ToString()}, {RestMethod.Post.ToString()} , {RestMethod.Put.ToString()}, {RestMethod.Patch.ToString()} and {RestMethod.Delete.ToString()}.");
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
        public static RestMethod[] CreateRestMethods(IEnumerable<string> methods)
        {
            List<RestMethod> restMethods = new();

            foreach (string method in methods)
            {
                RestMethod restMethod;
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
        /// The metod returns true/false corresponding to successful/unsuccessful conversion.
        /// </summary>
        /// <param name="operation">GraphQL operation configured for the stored procedure</param>
        /// <param name="graphQLOperation">GraphQL Operation as an Enum type</param>
        /// <returns>true/false</returns>
        public static bool TryConvertGraphQLOperationNameToGraphQLOperation(string? operation, [NotNullWhen(true)] out GraphQLOperation graphQLOperation)
        {
            if (!Enum.TryParse(operation, ignoreCase: true, out graphQLOperation))
            {
                _logger.LogError($"Invalid GraphQL Operation. Supported operations are {GraphQLOperation.Query.ToString()!.ToLower()} and {GraphQLOperation.Mutation.ToString()!.ToLower()!}.");
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
            SourceTypeEnumConverter.TryGetSourceType(options.SourceType, out SourceType sourceObjectType);
            return sourceObjectType is SourceType.StoredProcedure;
        }

        /// <summary>
        /// Method to determine whether the type of an entity is being converted from stored-procedure to
        /// table/view.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public static bool IsStoredProcedure(Entity entity)
        {
            return entity.ObjectType is SourceType.StoredProcedure;
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
        /// <returns>Constructed REST Path</returns>
        public static object? ConstructRestPathDetails(string? restRoute)
        {
            object? restPath;
            if (restRoute is null)
            {
                restPath = null;
            }
            else
            {
                if (bool.TryParse(restRoute, out bool restEnabled))
                {
                    restPath = restEnabled;
                }
                else
                {
                    restPath = "/" + restRoute;
                }
            }

            return restPath;
        }

        /// <summary>
        /// Constructs the graphQL Type from add/update command --graphql option
        /// </summary>
        /// <param name="graphQL">GraphQL type input from the CLI commands</param>
        /// <returns>Constructed GraphQL Type</returns>
        public static object? ConstructGraphQLTypeDetails(string? graphQL)
        {
            object? graphQLType;
            if (graphQL is null)
            {
                graphQLType = null;
            }
            else
            {
                if (bool.TryParse(graphQL, out bool graphQLEnabled))
                {
                    graphQLType = graphQLEnabled;
                }
                else
                {
                    string singular, plural;
                    if (graphQL.Contains(SEPARATOR))
                    {
                        string[] arr = graphQL.Split(SEPARATOR);
                        if (arr.Length != 2)
                        {
                            _logger.LogError($"Invalid format for --graphql. Accepted values are true/false," +
                                                    "a string, or a pair of string in the format <singular>:<plural>");
                            return null;
                        }

                        singular = arr[0];
                        plural = arr[1];
                    }
                    else
                    {
                        singular = graphQL.Singularize(inputIsKnownToBePlural: false);
                        plural = graphQL.Pluralize(inputIsKnownToBeSingular: false);
                    }

                    graphQLType = new SingularPlural(singular, plural);
                }
            }

            return graphQLType;
        }
    }
}
