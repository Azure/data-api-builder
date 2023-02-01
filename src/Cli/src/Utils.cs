using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Azure.DataApiBuilder.Config;
using Humanizer;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Config.AuthenticationConfig;
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
        public const string DEFAULT_VERSION = "1.0.0";

        #pragma warning disable CS8618
        // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private static ILogger<Utils> _logger;
        #pragma warning restore CS8618

        public static void SetCliUtilsLogger(ILogger<Utils> cliUtilsLogger)
        {
            _logger = cliUtilsLogger;
        }

        /// <summary>
        /// Reads the product version from the executing assembly's file version information.
        /// </summary>
        /// <returns>Product version if not null, default version 1.0.0 otherwise.</returns>
        public static string GetProductVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            string? version = fileVersionInfo.ProductVersion;

            return version ?? DEFAULT_VERSION;
        }

        /// <summary>
        /// Creates the rest object which can be either a boolean value
        /// or a RestEntitySettings object containing api route based on the input
        /// </summary>
        public static object? GetRestDetails(string? rest)
        {
            object? rest_detail;
            if (rest is null)
            {
                return rest;
            }

            bool trueOrFalse;
            if (bool.TryParse(rest, out trueOrFalse))
            {
                rest_detail = trueOrFalse;
            }
            else
            {
                RestEntitySettings restEntitySettings = new("/" + rest);
                rest_detail = restEntitySettings;
            }

            return rest_detail;
        }

        /// <summary>
        /// Creates the graphql object which can be either a boolean value
        /// or a GraphQLEntitySettings object containing graphql type {singular, plural} based on the input
        /// </summary>
        public static object? GetGraphQLDetails(string? graphQL)
        {
            object? graphQL_detail;
            if (graphQL is null)
            {
                return graphQL;
            }

            bool trueOrFalse;
            if (bool.TryParse(graphQL, out trueOrFalse))
            {
                graphQL_detail = trueOrFalse;
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

                SingularPlural singularPlural = new(singular, plural);
                GraphQLEntitySettings graphQLEntitySettings = new(singularPlural);
                graphQL_detail = graphQLEntitySettings;
            }

            return graphQL_detail;
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
        public static IDictionary<Operation, PermissionOperation> ConvertOperationArrayToIEnumerable(object[] operations)
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
                            // Expand wildcard to all valid operations
                            foreach (Operation validOp in PermissionOperation.ValidPermissionOperations)
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
                        // Expand wildcard to all valid operations.
                        foreach (Operation validOp in PermissionOperation.ValidPermissionOperations)
                        {
                            result.Add(
                                validOp,
                                new PermissionOperation(validOp, Policy: ac.Policy, Fields: ac.Fields));
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
        /// Ignoring properties with null values.
        /// Keeping all the keys in lowercase.
        /// </summary>
        public static JsonSerializerOptions GetSerializationOptions()
        {
            JsonSerializerOptions? options = new()
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
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
                                                                                      string? issuer = null)
        {
            Dictionary<GlobalSettingsType, object> defaultGlobalSettings = new();
            defaultGlobalSettings.Add(GlobalSettingsType.Rest, new RestGlobalSettings());
            defaultGlobalSettings.Add(GlobalSettingsType.GraphQL, new GraphQLGlobalSettings());
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

            // Currently, Stored Procedures can be configured with only 1 CRUD Operation.
            if (sourceType is SourceType.StoredProcedure
                    && !VerifySingleOperationForStoredProcedure(operations))
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
                    else if (!PermissionOperation.ValidPermissionOperations.Contains(op))
                    {
                        _logger.LogError("Invalid actions found in --permissions");
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
        /// Checks if config can be correctly parsed by deserializing the
        /// json config into runtime config object.
        /// Also checks that connection-string is not null or empty whitespace
        /// </summary>
        public static bool CanParseConfigCorrectly(string configFile)
        {
            if (!TryReadRuntimeConfig(configFile, out string runtimeConfigJson))
            {
                _logger.LogError($"Failed to read the config file: {configFile}.");
                return false;
            }

            if (!RuntimeConfig.TryGetDeserializedRuntimeConfig(
                    runtimeConfigJson,
                    out RuntimeConfig? deserializedRuntimeConfig,
                    logger: null))
            {
                _logger.LogError($"Failed to parse the config file: {configFile}.");
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
        /// key-fields only with table/views.
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
            if (SourceType.StoredProcedure.Equals(sourceType))
            {
                if (keyFields is not null && keyFields.Any())
                {
                    _logger.LogError("Stored Procedures don't support keyfields.");
                    return false;
                }
            }
            else
            {
                if (parameters is not null && parameters.Any())
                {
                    _logger.LogError("Tables/Views don't support parameters.");
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
                if (!VerifySingleOperationForStoredProcedure(permissionSetting.Operations))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// This method checks that stored-procedure entity
        /// has only one CRUD operation.
        /// </summary>
        private static bool VerifySingleOperationForStoredProcedure(object[] operations)
        {
            if (operations.Length > 1
                || !TryGetOperationName(operations.First(), out Operation operationName)
                || Operation.All.Equals(operationName))
            {
                _logger.LogError("Stored Procedure supports only 1 CRUD operation.");
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
    }
}
