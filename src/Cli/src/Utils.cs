using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Azure.DataApiBuilder.Config;
using Humanizer;
using Action = Azure.DataApiBuilder.Config.Action;

/// <summary>
/// Contains the methods for transforming objects, serialization options.
/// </summary>
namespace Cli
{
    public class Utils
    {
        public const string WILDCARD = "*";

        /// <summary>
        /// creates the rest object which can be either a boolean value
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
        /// creates the graphql object which can be either a boolean value
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
                if (graphQL.Contains(":"))
                {
                    string[] arr = graphQL.Split(":");
                    if (arr.Length != 2)
                    {
                        Console.Error.WriteLine($"Invalid format for --graphql. Accepted values are true/false," +
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
        /// Try convert action string to Operation Enum.
        /// </summary>
        /// <param name="actionName">Action string.</param>
        /// <param name="operation">Operation Enum output.</param>
        /// <returns>True if convert is successful. False otherwise.</returns>
        public static bool TryConvertActionNameToOperation(string actionName, out Operation operation)
        {
            if (!Enum.TryParse(actionName, ignoreCase: true, out operation))
            {
                if (actionName.Equals(WILDCARD, StringComparison.OrdinalIgnoreCase))
                {
                    operation = Operation.All;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// creates an array of Action element which contains one of the CRUD operation and
        /// fields to which this action is allowed as permission setting based on the given input.
        /// </summary>
        public static object[] CreateActions(string actions, Policy? policy, Field? fields)
        {
            object[] action_items;
            if (policy is null && fields is null)
            {
                return actions.Split(",");
            }

            if (actions is WILDCARD)
            {
                action_items = new object[] { new Action(Operation.All, policy, fields) };
            }
            else
            {
                string[]? action_elements = actions.Split(",");
                if (policy is not null || fields is not null)
                {
                    List<object>? action_list = new();
                    foreach (string? action_element in action_elements)
                    {
                        if (TryConvertActionNameToOperation(action_element, out Operation op))
                        {
                            Action? action_item = new(op, policy, fields);
                            action_list.Add(action_item);
                        }
                    }

                    action_items = action_list.ToArray();
                }
                else
                {
                    action_items = action_elements;
                }
            }

            return action_items;
        }

        /// <summary>
        /// Given an array of actions, which is a type of JsonElement, convert it to a dictionary
        /// key: Valid action operation (wild card operation will be expanded)
        /// value: Action object
        /// </summary>
        /// <param name="Actions">Array of actions which is of type JsonElement.</param>
        /// <returns>Dictionary of actions</returns>
        public static IDictionary<Operation, Action> ConvertActionArrayToIEnumerable(object[] Actions)
        {
            Dictionary<Operation, Action> result = new();
            foreach (object action in Actions)
            {
                JsonElement actionJson = (JsonElement)action;
                if (actionJson.ValueKind is JsonValueKind.String)
                {
                    if (TryConvertActionNameToOperation(actionJson.GetString(), out Operation op))
                    {
                        if (op is Operation.All)
                        {
                            // Expand wildcard to all valid actions
                            foreach (Operation validOp in Action.ValidPermissionActions)
                            {
                                result.Add(validOp, new Action(validOp, null, null));
                            }
                        }
                        else
                        {
                            result.Add(op, new Action(op, null, null));
                        }
                    }
                }
                else
                {
                    Action ac = actionJson.Deserialize<Action>(GetSerializationOptions())!;

                    if (ac.Name is Operation.All)
                    {
                        // Expand wildcard to all valid actions
                        foreach (Operation validOp in Action.ValidPermissionActions)
                        {
                            result.Add(validOp, new Action(validOp, Policy: ac.Policy, Fields: ac.Fields));
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
        /// creates a single PermissionSetting Object based on role, actions, fieldsToInclude, and fieldsToExclude.
        /// </summary>
        public static PermissionSetting CreatePermissions(string role, string actions, Policy? policy, Field? fields)
        {
            return new PermissionSetting(role, CreateActions(actions, policy, fields));
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
                PropertyNamingPolicy = new LowerCaseNamingPolicy()
            };

            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: new LowerCaseNamingPolicy()));
            return options;
        }

        /// <summary>
        /// return true on successful parsing of mappings Dictionary from IEnumerable list.
        /// returns false in case the format of the input is not correct.
        /// </summary>
        /// <param name="mappingList">List of ':' separated values indicating exposed and backend names.</param>
        /// <param name="mappings">Output a Dictionary containing mapping from backend name to exposed name.</param>
        /// <returns> Returns true when successful else on failure, returns false. Else updated PermissionSettings array will be returned.</returns>
        public static bool TryParseMappingDictionary(IEnumerable<string> mappingList, out Dictionary<string, string> mappings)
        {
            mappings = new();
            foreach (string item in mappingList)
            {
                string[] map = item.Split(":");
                if (map.Length != 2)
                {
                    Console.Error.WriteLine("Invalid format for --map");
                    Console.WriteLine("It should be in this format --map \"backendName1:exposedName1,backendName2:exposedName2,...\".");
                    return false;
                }

                mappings.Add(map[0], map[1]);
            }

            return true;
        }

        /// <summary>
        /// returns the default global settings based on dbType.
        /// </summary>
        public static Dictionary<GlobalSettingsType, object> GetDefaultGlobalSettings(DatabaseType dbType,
                                                                                        HostModeType hostMode,
                                                                                        IEnumerable<string>? corsOrigin)
        {
            Dictionary<GlobalSettingsType, object> defaultGlobalSettings = new();
            if (DatabaseType.cosmos.Equals(dbType))
            {
                defaultGlobalSettings.Add(GlobalSettingsType.Rest, new RestGlobalSettings(Enabled: false));
            }
            else
            {
                defaultGlobalSettings.Add(GlobalSettingsType.Rest, new RestGlobalSettings());
            }

            defaultGlobalSettings.Add(GlobalSettingsType.GraphQL, new GraphQLGlobalSettings());
            defaultGlobalSettings.Add(GlobalSettingsType.Host, GetDefaultHostGlobalSettings(hostMode, corsOrigin));
            return defaultGlobalSettings;
        }

        /// <summary>
        /// returns the default host Global Settings
        /// if the user doesn't specify host mode. Default value to be used is Production.
        /// sample:
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
        public static HostGlobalSettings GetDefaultHostGlobalSettings(HostModeType hostMode, IEnumerable<string>? corsOrigin)
        {
            string[]? corsOriginArray = corsOrigin is null ? new string[] { } : corsOrigin.ToArray();
            Cors cors = new(Origins: corsOriginArray);
            AuthenticationConfig authenticationConfig = new(Provider: EasyAuthType.StaticWebApps.ToString());
            return new HostGlobalSettings(hostMode, cors, authenticationConfig);
        }

        /// <summary>
        /// returns an object of type Policy
        /// if policyRequest or policyDatabase is provided. Otherwise, returns null.
        /// </summary>
        public static Policy? GetPolicyForAction(string? policyRequest, string? policyDatabase)
        {
            if (policyRequest is not null || policyDatabase is not null)
            {
                return new Policy(policyRequest, policyDatabase);
            }

            return null;
        }

        /// <summary>
        /// returns an object of type Field
        /// if fieldsToInclude or fieldsToExclude is provided. Otherwise, returns null.
        /// </summary>
        public static Field? GetFieldsForAction(IEnumerable<string>? fieldsToInclude, IEnumerable<string>? fieldsToExclude)
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
                Console.WriteLine($"ERROR: Couldn't find config  file: {file}.");
                Console.WriteLine($"Please run: hawaii init <options> to create a new config file.");

                return false;
            }

            // Read existing config file content.
            //
            runtimeConfigJson = File.ReadAllText(file);
            return true;
        }

        /// <summary>
        /// Verifies whether the action provided by the user is valid or not
        /// Example:
        /// *, create -> Invalid
        /// create, create, read -> Invalid
        /// * -> Valid
        /// fetch, read -> Invalid
        /// read, delete -> Valid
        /// </summary>
        /// <param name="actions">array of string containing actions for permissions</param>
        /// <returns>True if no invalid action is found.</returns>
        public static bool VerifyActions(string[] actions)
        {
            // Check if there are any duplicate actions
            // Ex: read,read,create
            HashSet<string> uniqueActions = actions.ToHashSet();
            if (uniqueActions.Count() != actions.Length)
            {
                Console.Error.WriteLine("Duplicate action found in --permissions");
                return false;
            }

            bool containsWildcardAction = false;
            foreach (string action in uniqueActions)
            {
                if (TryConvertActionNameToOperation(action, out Operation op))
                {
                    if (op is Operation.All)
                    {
                        containsWildcardAction = true;
                    }
                    else if (!Action.ValidPermissionActions.Contains(op))
                    {
                        Console.Error.WriteLine("Invalid actions found in --permissions");
                        return false;
                    }
                }
                else
                {
                    // Check for invalid actions.
                    Console.Error.WriteLine("Invalid actions found in --permissions");
                    return false;
                }
            }

            // Check for WILDCARD action with CRUD actions
            if (containsWildcardAction && uniqueActions.Count() > 1)
            {
                Console.Error.WriteLine(" WILDCARD(*) along with other CRUD actions in a single operation is not allowed.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// this method will parse role and Action from permission string.
        /// A valid permission string will be of the form "<<role>>:<<actions>>"
        /// it will return true if parsing is successful and add the parsed value
        /// to the out params role and action.
        /// </summary>
        public static bool TryGetRoleAndActionFromPermission(IEnumerable<string> permissions, out string? role, out string? actions)
        {
            // Split permission to role and actions
            //
            role = null;
            actions = null;
            if (permissions.Count() != 2)
            {
                Console.WriteLine("Please add permission in the following format. --permissions \"<<role>>:<<actions>>\"");
                return false;
            }

            role = permissions.ElementAt(0);
            actions = permissions.ElementAt(1);
            return true;
        }

        /// <summary>
        /// This method will try to find the config file based on the precedence.
        /// if config file provided by user, it will return that.
        /// Else it will check the DAB_ENVIRONMENT variable.
        /// In case the environment variable is not set it will check for default config.
        /// If none of the file exists it will return false. Else true with output in runtimeConfigFile.
        /// In case of false, the runtimeConfigFile will be set to string.Empty.
        /// </summary>
        public static bool TryGetConfigFileBasedOnCliPrecedence(
            string? userProvidedConfigFile,
            out string runtimeConfigFile)
        {
            if (!string.IsNullOrEmpty(userProvidedConfigFile) && File.Exists(userProvidedConfigFile))
            {
                RuntimeConfigPath.CheckPrecedenceForConfigInEngine = false;
                runtimeConfigFile = userProvidedConfigFile;
                return true;
            }
            else
            {
                Console.WriteLine("Config not provided. Trying to get default config based on DAB_ENVIRONMENT...");
                RuntimeConfigPath.CheckPrecedenceForConfigInEngine = true;
                runtimeConfigFile = RuntimeConfigPath.GetFileNameForEnvironment(
                        hostingEnvironmentName: null,
                        considerOverrides: false);

                // so that the check doesn't run again when starting engine
                RuntimeConfigPath.CheckPrecedenceForConfigInEngine = false;
            }

            return !string.IsNullOrEmpty(runtimeConfigFile);
        }

        /// <summary>
        /// this method will write all the json string in the given file.
        /// </summary>
        public static bool WriteJsonContentToFile(string file, string jsonContent)
        {
            try
            {
                File.WriteAllText(file, jsonContent);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to generate the config file, operation failed with exception:{e}.");
                return false;
            }

            return true;
        }
    }
}
