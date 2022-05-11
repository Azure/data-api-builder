using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Config;
using Humanizer;
using System.Runtime.Serialization;

namespace Hawaii.Cli.Classes
{
    public class Action {
        public string? action {get; set;} = null;
        public Dictionary<string, string[]>? fields {get; set;} = null;

        // public Action(string action, Dictionary<string,string[]> fields) {
        //     this.action = action;
        //     this.fields = fields;
        // }
        public static Action GetAction(string action, Dictionary<string,string[]> fields) {
            Action actionObject = new Action();
            actionObject.action = action;
            actionObject.fields = fields;
            return actionObject;
        }
        
        public static Action GetAction(string action, string? fieldsToInclude, string? fieldsToExclude) {
            Action actionObject = new Action();
            actionObject.action = action;
            actionObject.fields = new Dictionary<string, string[]>();
            if(fieldsToInclude!=null) {
                actionObject.fields.Add("include", fieldsToInclude.Split(","));
            }
            if(fieldsToExclude!=null) {
                actionObject.fields.Add("exclude", fieldsToExclude.Split(","));
            }
            return actionObject;
        }

        public static Action ToObject(JsonElement element)
        {
            if(JsonValueKind.String.Equals(element.ValueKind)) {
                return Action.GetAction(element.ToString(), null);
            }
            var json = element.GetRawText();
            return JsonSerializer.Deserialize<Action>(json);
        }
    }

    public enum CRUD {
        create,
        read,
        update,
        delete
    }
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
                action_items =  new object[]{Action.GetAction(actions, fieldsToInclude, fieldsToExclude)};
            } else {
                string[] action_elements = actions.Split(",");
                //#action_items should be 1, if either fieldsTOInclude or fieldsToExclude is not null.
                if(fieldsToInclude!=null || fieldsToExclude!=null ) {
                    List<object> action_list = new List<object>();
                    foreach(string action_element in action_elements) {
                        Action action_item = Action.GetAction(action_element, fieldsToInclude, fieldsToExclude);
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

        public static PermissionSetting[] AddNewPermissions(PermissionSetting[] currentPermissions, string role, string actions, string? fieldsToInclude, string? fieldsToExclude) {
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

        public static string GetCRUDOperation(JsonElement op) {
            if(JsonValueKind.String.Equals(op.ValueKind)) {
                return op.ToString();
            }
            return (Action.ToObject(op)).action;
        }

        // public static Boolean IsString(JsonElement action) {

        // }

        // public static Boolean IsOneOfCRUDOperation(JsonElement action) {
        //     if((JsonValueKind.String.Equals(action.ValueKind)) && ("create".Equals(action.ToString()) || "read".Equals(action.ToString()) || "update".Equals(action.ToString()) || "delete".Equals(action.ToString()))) {
        //         return true;
        //     }
        //     return false;
        // }

        // public static Action ToObject(JsonElement element)
        // {
        //     if(JsonValueKind.String.Equals(element.ValueKind)) {
        //         return Action.GetAction(element.ToString(), new Dictionary<string, string[]>());
        //     }
        //     var json = element.GetRawText();
        //     return JsonSerializer.Deserialize<Action>(json);
        // }
    }
}