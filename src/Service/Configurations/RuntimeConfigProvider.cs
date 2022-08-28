using System;
using System.IO;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Configurations
{
    /// <summary>
    /// This class provides access to the runtime configuration and provides a change notification
    /// in the case where the runtime is started without the configuration so it can be set later.
    /// </summary>
    public class RuntimeConfigProvider
    {
        public event EventHandler<RuntimeConfig>? RuntimeConfigLoaded;

        /// <summary>
        /// The config provider logger is a static member because we use it in static methods
        /// like LoadRuntimeConfigValue, GetRuntimeConfigJsonString which themselves are static
        /// to be used by tests.
        /// </summary>
        public static ILogger<RuntimeConfigProvider>? ConfigProviderLogger;

        /// <summary>
        /// Represents the path to the runtime configuration file.
        /// </summary>
        protected RuntimeConfigPath? RuntimeConfigPath { get; private set; }

        /// <summary>
        /// Represents the loaded and deserialized runtime configuration.
        /// </summary>
        protected virtual RuntimeConfig? RuntimeConfiguration { get; private set; }

        public virtual string RestPath
        {
            get { return RuntimeConfiguration is not null ? RuntimeConfiguration.RestGlobalSettings.Path : string.Empty; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeConfigProvider"/> class.
        /// </summary>
        /// <param name="runtimeConfigPath"></param>
        /// <param name="logger"></param>
        public RuntimeConfigProvider(
            RuntimeConfigPath runtimeConfigPath,
            ILogger<RuntimeConfigProvider> logger)
        {
            RuntimeConfigPath = runtimeConfigPath;

            if (ConfigProviderLogger is null)
            {
                ConfigProviderLogger = logger;
            }

            if (TryLoadRuntimeConfigValue())
            {
                ConfigProviderLogger.LogInformation("Runtime config loaded from file.");
            }
            else
            {
                ConfigProviderLogger.LogInformation("Runtime config provided didn't load config at construction.");
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeConfigProvider"/> class.
        /// </summary>
        /// <param name="runtimeConfig"></param>
        /// <param name="logger"></param>
        public RuntimeConfigProvider(
            RuntimeConfig runtimeConfig,
            ILogger<RuntimeConfigProvider> logger)
        {
            RuntimeConfiguration = runtimeConfig;

            if (ConfigProviderLogger is null)
            {
                ConfigProviderLogger = logger;
            }

            ConfigProviderLogger.LogInformation("Using the provided runtime configuration object.");
        }

        /// <summary>
        /// If the RuntimeConfiguration is not already loaded, tries to load it.
        /// Returns a true in case of already loaded or a successful load, otherwise false.
        /// Catches any exceptions that arise while loading.
        /// </summary>
        public virtual bool TryLoadRuntimeConfigValue()
        {
            try
            {
                if (RuntimeConfiguration is null &&
                   LoadRuntimeConfigValue(RuntimeConfigPath, out RuntimeConfig? runtimeConfig))
                {
                    RuntimeConfiguration = runtimeConfig;
                }

                return RuntimeConfiguration is not null;
            }
            catch (Exception ex)
            {
                ConfigProviderLogger!.LogError($"Failed to load the runtime" +
                    $" configuration file due to: \n{ex}");
            }

            return false;
        }

        /// <summary>
        /// Reads the contents of the json config file if it exists,
        /// and sets the deserialized RuntimeConfig object.
        /// </summary>
        public static bool LoadRuntimeConfigValue(
            RuntimeConfigPath? configPath,
            out RuntimeConfig? runtimeConfig)
        {
            string? configFileName = configPath?.ConfigFileName;
            string? runtimeConfigJson = GetRuntimeConfigJsonString(configFileName);

            if (!string.IsNullOrEmpty(runtimeConfigJson) &&
                RuntimeConfig.TryGetDeserializedConfig(
                    runtimeConfigJson,
                    out runtimeConfig))
            {
                runtimeConfig!.DetermineGlobalSettings();
                runtimeConfig!.DetermineGraphQLEntityNames();

                if (!string.IsNullOrWhiteSpace(configPath?.CONNSTRING))
                {
                    runtimeConfig!.ConnectionString = configPath.CONNSTRING;
                }

                if (ConfigProviderLogger is not null)
                {
                    ConfigProviderLogger.LogInformation($"Runtime configuration has been successfully loaded.");
                }

                return true;
            }

            runtimeConfig = null;
            return false;
        }

        /// <summary>
        /// Reads the string from the given file name, replaces any environment variables
        /// and returns the parsed string.
        /// </summary>
        /// <param name="configFileName"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public static string? GetRuntimeConfigJsonString(string? configFileName)
        {
            string? runtimeConfigJson;
            if (!string.IsNullOrEmpty(configFileName))
            {
                if (File.Exists(configFileName))
                {
                    if (ConfigProviderLogger is not null)
                    {
                        ConfigProviderLogger.LogInformation($"Using file {configFileName} to configure the runtime.");
                    }

                    runtimeConfigJson = RuntimeConfigPath.ParseConfigJsonAndReplaceEnvVariables(File.ReadAllText(configFileName));
                }
                else
                {
                    // This is the case when config file name provided as a commandLine argument
                    // does not exist.
                    throw new FileNotFoundException($"Requested configuration file '{configFileName}' does not exist.");
                }
            }
            else
            {
                // This is the case when GetFileNameForEnvironment() is unable to
                // find a configuration file name after attempting all the possibilities
                // and checking for their existence in the current directory
                // eventually setting it to an empty string.
                throw new ArgumentNullException("Configuration file name",
                    $"Could not determine a configuration file name that exists.");
            }

            return runtimeConfigJson;
        }

        /// <summary>
        /// Initialize the runtime configuration provider with the specified configurations.
        /// This initialization method is used when the configuration is sent to the ConfigurationController
        /// in the form of a string instead of reading the configuration from a configuration file.
        /// </summary>
        /// <param name="configuration">The engine configuration.</param>
        /// <param name="schema">The GraphQL Schema. Can be left null for SQL configurations.</param>
        /// <param name="connectionString">The connection string to the database.</param>
        public void Initialize(string configuration, string? schema, string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException($"'{nameof(connectionString)}' cannot be null or empty.", nameof(connectionString));
            }

            if (string.IsNullOrEmpty(configuration))
            {
                throw new ArgumentException($"'{nameof(configuration)}' cannot be null or empty.", nameof(configuration));
            }

            if (RuntimeConfig.TryGetDeserializedConfig(
                    configuration,
                    out RuntimeConfig? runtimeConfig))
            {
                RuntimeConfiguration = runtimeConfig;
                RuntimeConfiguration!.DetermineGlobalSettings();
                RuntimeConfiguration!.DetermineGraphQLEntityNames();
                RuntimeConfiguration!.ConnectionString = connectionString;

                if (RuntimeConfiguration!.DatabaseType == DatabaseType.cosmos)
                {
                    if (string.IsNullOrEmpty(schema))
                    {
                        throw new ArgumentException($"'{nameof(schema)}' cannot be null or empty.", nameof(schema));
                    }

                    CosmosDbOptions? cosmosDb = RuntimeConfiguration.CosmosDb! with { GraphQLSchema = schema };
                    RuntimeConfiguration = RuntimeConfiguration with { CosmosDb = cosmosDb };
                }
            }

            EventHandler<RuntimeConfig>? handlers = RuntimeConfigLoaded;
            if (handlers != null)
            {
                handlers(this, RuntimeConfiguration);
            }
        }

        public virtual RuntimeConfig GetRuntimeConfiguration()
        {
            if (RuntimeConfiguration is null)
            {
                throw new DataApiBuilderException(
                    message: "Runtime config isn't setup.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            return RuntimeConfiguration;
        }

        public virtual bool TryGetRuntimeConfiguration(out RuntimeConfig? runtimeConfig)
        {
            runtimeConfig = RuntimeConfiguration;
            return RuntimeConfiguration is not null;
        }

        public virtual bool IsDeveloperMode()
        {
            return RuntimeConfiguration?.HostGlobalSettings.Mode is HostModeType.Development;
        }

        /// <summary>
        /// When we are in development mode, we want to honor the default-request-authorization
        /// feature switch value specified in the config file. This gives us the ability to
        /// simulate authenticated/anonymous authentication state of request in development mode.
        /// </summary>
        /// <returns></returns>
        public virtual bool IsAuthenticatedDevModeRequest()
        {
            if (RuntimeConfiguration is null)
            {
                return false;
            }

            return IsDeveloperMode() &&
                RuntimeConfiguration.HostGlobalSettings.IsDevModeDefaultRequestAuthenticated is true;
        }
    }
}
