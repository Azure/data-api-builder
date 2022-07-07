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
            if (rest is null || bool.TryParse(rest, out _))
            {
                rest_detail = rest;
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
            if (graphQL is null || bool.TryParse(graphQL, out _))
            {
                graphQL_detail = graphQL;
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
        /// creates an Action element which contains one of the CRUD operation and
        /// fields to which this action is allowed as permission setting.
        /// </summary>
        public static Action GetAction(string action, string? fieldsToInclude, string? fieldsToExclude)
        {
            Action? actionObject = new(action, Policy: null, Fields: null);
            if (fieldsToInclude is not null || fieldsToExclude is not null)
            {
                string[]? fieldsToIncludeArray = fieldsToInclude is not null ? fieldsToInclude.Split(",") : null;
                string[]? fieldsToExcludeArray = fieldsToExclude is not null ? fieldsToExclude.Split(",") : null;
                actionObject = new Action(action, Policy: null, Fields: new Field(fieldsToIncludeArray, fieldsToExcludeArray));
            }

            return actionObject;
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
        public static object[] CreateActions(string actions, string? fieldsToInclude, string? fieldsToExclude)
        {
            object[] action_items;
            if (fieldsToInclude is null && fieldsToExclude is null)
            {
                return actions.Split(",");
            }

            if (actions is WILDCARD)
            {
                action_items = new object[] { GetAction(actions, fieldsToInclude, fieldsToExclude) };
            }
            else
            {
                string[]? action_elements = actions.Split(",");
                if (fieldsToInclude is not null || fieldsToExclude is not null)
                {
                    List<object>? action_list = new();
                    foreach (string? action_element in action_elements)
                    {
                        Action? action_item = GetAction(action_element, fieldsToInclude, fieldsToExclude);
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
        /// creates a single PermissionSetting Object based on role, actions, fieldsToInclude, and fieldsToExclude.
        /// </summary>
        public static PermissionSetting CreatePermissions(string role, string actions, string? fieldsToInclude, string? fieldsToExclude)
        {
            return new PermissionSetting(role, CreateActions(actions, fieldsToInclude, fieldsToExclude));
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
        /// returns the default global settings based on dbType.
        /// </summary>
        public static Dictionary<GlobalSettingsType, object> GetDefaultGlobalSettings(DatabaseType dbType, HostModeType hostMode)
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
            defaultGlobalSettings.Add(GlobalSettingsType.Host, GetDefaultHostGlobalSettings(hostMode));
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
        //         "provider": "EasyAuth"
        //     }
        // }
        /// </summary>
        public static HostGlobalSettings GetDefaultHostGlobalSettings(HostModeType hostMode)
        {
            Cors cors = new(new string[] { });
            AuthenticationConfig authenticationConfig = new();
            return new HostGlobalSettings(hostMode, cors, authenticationConfig);
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
        /// this method will parse role and Action from permission string.
        /// A valid permission string will be of the form "<<role>>:<<actions>>"
        /// it will return true if parsing is successful and add the parsed value
        /// to the out params role and action.
        /// </summary>
        public static bool TryGetRoleAndActionFromPermissionString(string permissions, out string? role, out string? actions)
        {
            // Split permission to role and actions
            //
            role = null;
            actions = null;
            string[] permission_array = permissions.Split(":");
            if (permission_array.Length != 2)
            {
                Console.WriteLine("Please add permission in the following format. --permission \"<<role>>:<<actions>>\"");
                return false;
            }

            role = permission_array[0];
            actions = permission_array[1];
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
