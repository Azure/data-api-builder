using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Config;
using Humanizer;
using Action = Azure.DataGateway.Config.Action;

/// <summary>
/// Contains the methods for transforming objects, serialization options.
/// </summary>
namespace Hawaii.Cli.Models
{
    public enum CRUD
    {
        create,
        read,
        update,
        delete
    }
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
                return rest;

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
        /// translates the JsonElement to the Action Object
        /// </summary>
        public static Action? ToActionObject(JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.String)
            {
                return new Action(element.GetRawText(), Policy: null, Fields: null);
            }

            string json = element.GetRawText();
            return JsonSerializer.Deserialize<Action>(json);
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
                action_items = new object[] { new Action(actions, policy, fields) };
            }
            else
            {
                string[]? action_elements = actions.Split(",");
                if (policy is not null || fields is not null)
                {
                    List<object>? action_list = new();
                    foreach (string? action_element in action_elements)
                    {
                        Action? action_item = new(action_element, policy, fields);
                        action_list.Add(action_item);
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
        /// creates a Dictionary with Key as the action name and value as the Action object
        /// </summary>
        public static Dictionary<string, Action> GetDictionaryFromActionObjectList(IEnumerable<object> actionList)
        {
            Dictionary<string, Action> actionMap = new();
            foreach (object action in actionList)
            {
                JsonElement actionJsonElement = JsonSerializer.SerializeToElement(action);
                string actionName = GetCRUDOperation(actionJsonElement);
                Action actionObject = ToActionObject(actionJsonElement)!;
                actionMap.Add(actionName, actionObject);
            }

            return actionMap;
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
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = new LowerCaseNamingPolicy()
            };

            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: new LowerCaseNamingPolicy()));
            return options;
        }

        /// <summary>
        /// returns the Action name from the parsed JsonElement from Config file.
        /// </summary>
        public static string GetCRUDOperation(JsonElement op)
        {
            if (op.ValueKind is JsonValueKind.String)
            {
                return op.ToString();
            }

            return ToActionObject(op)!.Name;
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
            if ((fieldsToInclude is not null && fieldsToInclude.Any()) || (fieldsToExclude is not null && fieldsToExclude.Any()))
            {
                HashSet<string>? fieldsToIncludeSet = (fieldsToInclude is not null && fieldsToInclude.Any()) ? new HashSet<string>(fieldsToInclude) : null;
                HashSet<string>? fieldsToExcludeSet = (fieldsToExclude is not null && fieldsToExclude.Any()) ? new HashSet<string>(fieldsToExclude) : null;
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
                CRUD crud;
                if (!Enum.TryParse<CRUD>(action.ToLower(), out crud))
                {
                    if (action is WILDCARD)
                    {
                        containsWildcardAction = true;
                    }
                    else
                    {
                        // Check for invalid CRUD actions such as fetch, creates, etc.
                        Console.Error.WriteLine("Invalid actions found in --permissions");
                        return false;
                    }
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
