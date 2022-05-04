namespace Azure.DataGateway.Config
{
    public class RuntimeConfigPath
    {
        public const string CONFIGFILE_NAME = "hawaii-config";
        public const string CONFIG_EXTENSION = ".json";

        public const string RUNTIME_ENVIRONMENT_VAR_NAME = "HAWAII_ENVIRONMENT";

        public string? ConfigFileName { get; set; }

        public RuntimeConfig? ConfigValue { get; set; }

        public static string DefaultName
        {
            get
            {
                return $"{CONFIGFILE_NAME}{CONFIG_EXTENSION}";
            }
        }

        /// <summary>
        /// Reads the contents of the json config file,
        /// and returns the deserialized RuntimeConfig object.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public void SetRuntimeConfigValue()
        {
            string? runtimeConfigJson = null;
            if (!string.IsNullOrEmpty(ConfigFileName) && File.Exists(ConfigFileName))
            {
                runtimeConfigJson = File.ReadAllText(ConfigFileName);
            }

            if (!string.IsNullOrEmpty(runtimeConfigJson))
            {
                ConfigValue = RuntimeConfig.GetDeserializedConfig<RuntimeConfig>(runtimeConfigJson);
            }
        }

        public static string GetFileNameAsPerEnvironment(string hostingEnvironmentName)
        {
            string? runtimeEnvironmentValue
                = Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME);
            if (runtimeEnvironmentValue != null)
            {
                return $"{CONFIGFILE_NAME}.{runtimeEnvironmentValue}{CONFIG_EXTENSION}";
            }
            else
            {
                return !string.IsNullOrWhiteSpace(hostingEnvironmentName)
                    ? $"{CONFIGFILE_NAME}.{hostingEnvironmentName}{CONFIG_EXTENSION}"
        :               $"{DefaultName}";
            }
        }
    }
}
