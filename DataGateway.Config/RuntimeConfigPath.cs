namespace Azure.DataGateway.Config
{
    /// <summary>
    /// This class encapsulates the path related properties of the RuntimeConfig.
    /// The config file name property is provided by either
    /// the in memory configuration provider, command line configuration provider
    /// or from the in memory updateable configuration controller.
    /// </summary>
    public class RuntimeConfigPath
    {
        public const string CONFIGFILE_NAME = "hawaii-config";
        public const string CONFIG_EXTENSION = ".json";

        public const string RUNTIME_ENVIRONMENT_VAR_NAME = "HAWAII_ENVIRONMENT";
        public const string ENVIRONMENT_PREFIX = "HAWAII_";

        public string? ConfigFileName { get; set; }

        public string? CONNSTRING { get; set; }

        /// <summary>
        /// Reads the contents of the json config file if it exists,
        /// and sets the deserialized RuntimeConfig object.
        /// </summary>
        public RuntimeConfig? LoadRuntimeConfigValue()
        {
            string? runtimeConfigJson = null;
            if (!string.IsNullOrEmpty(ConfigFileName) && File.Exists(ConfigFileName))
            {
                runtimeConfigJson = File.ReadAllText(ConfigFileName);
            }

            if (!string.IsNullOrEmpty(runtimeConfigJson))
            {
                RuntimeConfig configValue = RuntimeConfig.GetDeserializedConfig<RuntimeConfig>(runtimeConfigJson);
                configValue.DetermineGlobalSettings();

                if (!string.IsNullOrWhiteSpace(CONNSTRING))
                {
                    configValue.ConnectionString = CONNSTRING;
                }

                return configValue;
            }

            return null;
        }

        /// <summary>
        /// Precedence of environments is
        /// 1) Value of HAWAII_ENVIRONMENT.
        /// 2) Value of ASPNETCORE_ENVIRONMENT.
        /// 3) Default config file name.
        /// In each case, overidden file name takes precedence.
        /// The first file name that exists in current directory is returned.
        /// The fall back options are hawaii-config.overrides.json/hawaii-config.json
        /// If no file exists, this will return an empty string.
        /// </summary>
        /// <param name="hostingEnvironmentName">Value of ASPNETCORE_ENVIRONMENT variable</param>
        /// <returns></returns>
        public static string GetFileNameForEnvironment(string? hostingEnvironmentName)
        {
            string configFileNameWithExtension = string.Empty;
            string?[] environmentPrecedence = new[]
            {
                Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME),
                hostingEnvironmentName,
                string.Empty
            };

            for (short index = 0;
                index < environmentPrecedence.Length
                && string.IsNullOrEmpty(configFileNameWithExtension);
                index++)
            {
                if (!string.IsNullOrWhiteSpace(environmentPrecedence[index])
                    // The last index is for the default case - the last fallback option
                    // where environmentPrecedence[index] is string.Empty
                    // for that case, we still need to get the file name considering overrides
                    // so need to do an OR on the last index here
                    || index == environmentPrecedence.Length - 1)
                {
                    configFileNameWithExtension =
                        GetFileNameConsideringOverrides(environmentPrecedence[index]);
                }
            }

            return configFileNameWithExtension;
        }

        // Used for testing
        public static string DefaultName
        {
            get
            {
                return $"{CONFIGFILE_NAME}{CONFIG_EXTENSION}";
            }
        }

        /// <summary>
        /// Generates the config file name and a corresponding overridden file name,
        /// With precedence given to overridden file name, returns that name
        /// if the file exists in the current directory, else an empty string.
        /// </summary>
        /// <param name="environmentValue">Name of the environment to
        /// generate the config file name for.</param>
        /// <returns></returns>
        private static string GetFileNameConsideringOverrides(string? environmentValue)
        {
            string configFileName =
                !string.IsNullOrEmpty(environmentValue)
                ? $"{CONFIGFILE_NAME}.{environmentValue}"
                : $"{CONFIGFILE_NAME}";
            string overriddenConfigFileNameWithExtension = GetOverriddenName(configFileName);
            if (DoesFileExistInCurrentDirectory(overriddenConfigFileNameWithExtension))
            {
                return overriddenConfigFileNameWithExtension;
            }
            else if (DoesFileExistInCurrentDirectory($"{configFileName}{CONFIG_EXTENSION}"))
            {
                return $"{configFileName}{CONFIG_EXTENSION}";
            }
            else
            {
                return string.Empty;
            }
        }

        private static string GetOverriddenName(string fileName)
        {
            return $"{fileName}.overrides{CONFIG_EXTENSION}";
        }

        private static bool DoesFileExistInCurrentDirectory(string fileName)
        {
            string currentDir = Directory.GetCurrentDirectory();
            return File.Exists(Path.Combine(currentDir, fileName));
        }
    }
}
