using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Config;
using Humanizer;
using System.Runtime.Serialization;

namespace Hawaii.Cli.Classes
{
    public class Utils
    {
        public static Boolean IsBooleanValue(string str) {
            return str=="true" || str=="false";
        }

        public static object? GetRestDetails(object? rest) {
            object? rest_detail = null;
            if(rest == null || Utils.IsBooleanValue(rest.ToString())) {
                rest_detail = rest;
            }
            else {
                Dictionary<string, string> route_details = new Dictionary<string, string>();
                route_details.Add("route", "/"+rest);
                rest_detail = route_details;
            }
            return rest_detail;
        }

        public static object? GetGraphQLDetails(object? graphQL) {
            object? graphQL_detail = null;
            if(graphQL == null || Utils.IsBooleanValue(graphQL.ToString())) {
                graphQL_detail = graphQL;
            }
            else {
                Dictionary<string, object> type_details = new Dictionary<string, object>();
                Dictionary<string, string> singular_plural = new Dictionary<string, string>();
                string? graphQLType = graphQL.ToString();
                singular_plural.Add("singular", graphQLType.Singularize(inputIsKnownToBePlural:false));
                singular_plural.Add("plural", graphQLType.Pluralize(inputIsKnownToBeSingular:false));
                type_details.Add("type", singular_plural);
                graphQL_detail = type_details;
            }

            return graphQL_detail;
        }


        public static object[] CreateActions(string actions, string? fieldsToInclude, string? fieldsToExclude) {
            object[] action_items;
            if("*".Equals(actions)){
                action_items =  new string[]{"*"};
            } else {
                object[] action_elements = actions.Split(",");
                //#action_items should be 1, if either fieldsTOInclude or fieldsToExclude is not null.
                if(fieldsToInclude!=null || fieldsToExclude!=null ) {
                    List<object> action_list = new List<object>();
                    foreach(object action_element in action_elements) {
                        Dictionary<string,object> action_item = new Dictionary<string, object>();
                        Dictionary<string, string[]> fields_dict = new Dictionary<string, string[]>();
                        action_item.Add("action", action_element);
                        if(fieldsToInclude!=null) {
                            fields_dict.Add("include", fieldsToInclude.Split(","));
                        }
                        if(fieldsToExclude!=null) {
                            fields_dict.Add("exclude", fieldsToExclude.Split(","));
                        }
                        action_item.Add("fields", fields_dict);
                        action_list.Add(action_item);
                    }
                    action_items =  action_list.ToArray();
                } else {
                    action_items = action_elements;
                }
            }
            return action_items;
        }

        public static PermissionSetting CreatePermissions(string role, string actions, string? fieldsToInclude, string? fieldsToExclude) {
            return new PermissionSetting(role, CreateActions(actions, fieldsToInclude, fieldsToExclude));
        }

        public static PermissionSetting[] UpdatePermissions(PermissionSetting[] currentPermissions, string role, string actions, string? fieldsToInclude, string? fieldsToExclude) {
            List<PermissionSetting> currentPermissionsList = currentPermissions.ToList();
            PermissionSetting permissionSetting = CreatePermissions(role, actions, fieldsToInclude, fieldsToExclude);
            currentPermissionsList.Add(permissionSetting);
            return currentPermissionsList.ToArray();
        }

        public static JsonSerializerOptions GetSerializationOptions() {
            JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            options.Converters.Add(new JsonStringEnumConverter());

            return options;
        }

        public static Dictionary<GlobalSettingsType, GlobalSettings> GetRuntimeSettings() {
            Dictionary<GlobalSettingsType, GlobalSettings> runtimeSettings = new Dictionary<GlobalSettingsType, GlobalSettings>();

            runtimeSettings.Add(GlobalSettingsType.Rest, new RestGlobalSettings(true, "/api"));
            runtimeSettings.Add(GlobalSettingsType.GraphQL, new GraphQLGlobalSettings(true, "/graphql", true));
            runtimeSettings.Add(GlobalSettingsType.Host, new HostGlobalSettings(HostModeType.Development, null , new AuthenticationConfig()));
            Console.WriteLine(new RestGlobalSettings(true, "/api"));
            return runtimeSettings;
        }
    }
}