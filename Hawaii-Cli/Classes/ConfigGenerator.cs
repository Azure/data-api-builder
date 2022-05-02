using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hawaii.Cli.Classes
{
    public class ConfigGenerator
    {
        public static void generateConfig(string fileName, string database_type, string connection_string)
        {
            Config config = new Config();
            config.data_source.database_type = database_type;
            config.data_source.connection_string = connection_string;

            string JSONresult = JsonConvert.SerializeObject(config, Formatting.Indented);
            string configPath = "generatedConfigs/" + fileName + ".json";

            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }

            File.WriteAllText(configPath, JSONresult);

        }

        public static void addEntitiesToConfig(string fileName, string entity,
                                             string source, string permissions,
                                             string? rest_route, string? graphQL_type)
        {
            //TODO: fix the fileName issue. It should be picked up from argument and not hard coded.
            string configPath = "generatedConfigs/" + "todo-001" + ".json";

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Couldn't find config  file: {configPath}.");
                Console.WriteLine($"Please do hawaii init <options> to create a new config file.");
                return;
            }

            string jsonString = File.ReadAllText(configPath);

            var jObject = JObject.Parse(jsonString);
            jObject.Add("entities", JObject.FromObject(new()));

            Dictionary<string, Object> dict = new();
            if (rest_route != null)
            {
                dict.Add("rest", JObject.FromObject(new { route = $"/{rest_route}" }));
            }

            if (graphQL_type != null)
            {
                dict.Add("graphql", JObject.FromObject(new { type = new { singular = $"{graphQL_type}", plural = $"{graphQL_type}" } }));
            }

            dict.Add("source", source);
            string[] permission_array = permissions.Split(":");
            string permission = JsonConvert.SerializeObject(new Permission(permission_array[0], permission_array[1]));
            JArray jArray = new JArray();
            jArray.Add(JObject.Parse(permission));
            dict.Add("permissions", jArray);

            //TODO: add support for multiple entities.
            jObject["entities"][entity] = JObject.FromObject(dict);

            string JSONresult = JsonConvert.SerializeObject(jObject, Formatting.Indented);
            File.WriteAllText(configPath, JSONresult);

        }

    }
}