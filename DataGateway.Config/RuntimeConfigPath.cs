namespace Azure.DataGateway.Config
{
    public record RuntimeConfigPath(string ConfigFileName)
    {
        public const string CONFIGFILE_NAME = "hawaii-config";
        public const string CONFIG_EXTENSION = ".json";
        public const string CONFIGFILE_PROPERTY_NAME = "runtime-config-file";


        public const string RUNTIME_ENVIRONMENT_VAR_NAME = "HAWAII_ENVIRONMENT";
        public static string ENVIRONMENT_VAR_PREFIX = "HAWAII";

        public static string DefaultName
        {
            get
            {
                return $"{CONFIGFILE_NAME}{CONFIG_EXTENSION}";
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

        public RuntimeConfigPath()
            : this (ConfigFileName: DefaultName)
        {

        }
    }
}
