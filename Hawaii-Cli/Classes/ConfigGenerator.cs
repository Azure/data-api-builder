using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

namespace Hawaii.Cli.Classes
{
    public class ConfigGenerator
    {
        public static void generateConfig(string fileName, string database_type, string connection_string) {
            Config config = new Config();
            config.data_source.database_type = database_type;
            config.data_source.connection_string = connection_string;
            
            string JSONresult = JsonConvert.SerializeObject(config);
            string configPath = "generatedConfigs/" + fileName + ".json";

            if(File.Exists(configPath)) {
                File.Delete(configPath);
            }

            using (var tw = new StreamWriter(configPath, true)) {
                tw.WriteLine(JSONresult.ToString());
                tw.Close();
            }

        }
        
    }
}
