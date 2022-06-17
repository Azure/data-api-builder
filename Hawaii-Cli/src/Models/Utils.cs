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
                SingularPlural singularPlural = new(
                    graphQL.Singularize(inputIsKnownToBePlural: false),
                    graphQL.Pluralize(inputIsKnownToBeSingular: false));
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

            if ("*".Equals(actions))
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
        /// Add a new PermissionSetting object(based on role, actions, fieldsToInclude, and fieldsToExclude) in the existing array of PermissionSetting.
        /// returns the updated array of PermissionSetting.
        /// </summary>
        public static PermissionSetting[] AddNewPermissions(PermissionSetting[] currentPermissions, string role, string actions, string? fieldsToInclude, string? fieldsToExclude)
        {
            List<PermissionSetting>? currentPermissionsList = currentPermissions.ToList();
            PermissionSetting? permissionSetting = CreatePermissions(role, actions, fieldsToInclude, fieldsToExclude);
            currentPermissionsList.Add(permissionSetting);
            return currentPermissionsList.ToArray();
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
                PropertyNamingPolicy = new LowerCaseNamingPolicy(),
                PropertyNameCaseInsensitive = true
            };

            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: new LowerCaseNamingPolicy()));
            return options;
        }

        /// <summary>
        /// returns the Action name from the parsed JsonElement from Config file.
        /// </summary>
        public static string GetCRUDOperation(JsonElement op)
        {
            if (JsonValueKind.String.Equals(op.ValueKind))
            {
                return op.ToString();
            }

            return ToActionObject(op)!.Name;
        }

        /// <summary>
        /// returns the Cardinality from the given string(one, or many).
        /// </summary>
        public static Cardinality GetCardinalityTypeFromString(string cardinality)
        {
            if ("one".Equals(cardinality, StringComparison.OrdinalIgnoreCase))
                return Cardinality.One;
            else if ("many".Equals(cardinality, StringComparison.OrdinalIgnoreCase))
                return Cardinality.Many;
            else
            {
                throw new NotSupportedException();
            }
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
        //         "provider": "EasyAuth",
        //         "jwt": {
        //         "audience": "",
        //         "issuer": "",
        //         "issuerkey": ""
        //         }
        //     }
        // }
        /// </summary>
        public static HostGlobalSettings GetDefaultHostGlobalSettings(HostModeType hostMode)
        {
            Cors cors = new(new string[] { });
            AuthenticationConfig authenticationConfig = new(Jwt: new Jwt(Audience: string.Empty, Issuer: string.Empty, IssuerKey: string.Empty));
            return new HostGlobalSettings(hostMode, cors, authenticationConfig);
        }
    }
}
