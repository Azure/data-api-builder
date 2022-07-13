using System;
using System.IO;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Microsoft.Extensions.Logging;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// This class provides access to the runtime configuration and provides a change notification
    /// in the case where the runtime is started without the configuration so it can be set later.
    /// </summary>
    public class RuntimeConfigProvider
    {
        private readonly RuntimeConfigPath? _runtimeConfigPath;
        private readonly ILogger<RuntimeConfigProvider> _logger;

        public event EventHandler<RuntimeConfig>? RuntimeConfigLoaded;

        protected virtual RuntimeConfig? RuntimeConfiguration { get; private set; }

        public virtual string RestPath
        {
            get { return RuntimeConfiguration is not null ? RuntimeConfiguration.RestGlobalSettings.Path : string.Empty; }
        }

        public RuntimeConfigProvider(
            RuntimeConfigPath runtimeConfigPath,
            ILogger<RuntimeConfigProvider> logger)
        {
            _runtimeConfigPath = runtimeConfigPath!;
            _logger = logger;
            if (runtimeConfigPath != null && TryLoadRuntimeConfigValue())
            {
                _logger.LogInformation("Runtime config loaded from file.");
            }
            else
            {
                _logger.LogInformation("Runtime config provided didn't load config at construction.");
            }
        }

        public RuntimeConfigProvider(
            RuntimeConfig runtimeConfig,
            ILogger<RuntimeConfigProvider> logger)
        {
            RuntimeConfiguration = runtimeConfig;
            _logger = logger;
            _logger.LogInformation("Using the provided runtime configuration object.");
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
                return RuntimeConfiguration is not null || LoadRuntimeConfigValue();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load the runtime" +
                    $" configuration file due to: \n{ex}");
            }

            return false;
        }

        /// <summary>
        /// Reads the contents of the json config file if it exists,
        /// and sets the deserialized RuntimeConfig object.
        /// </summary>
        public virtual bool LoadRuntimeConfigValue()
        {
            string? configFileName = _runtimeConfigPath?.ConfigFileName;
            string? runtimeConfigJson = GetRuntimeConfigJsonString(configFileName);

            if (!string.IsNullOrEmpty(runtimeConfigJson) &&
                RuntimeConfig.TryGetDeserializedConfig(
                    runtimeConfigJson,
                    out RuntimeConfig? runtimeConfig))
            {
                RuntimeConfiguration = runtimeConfig;
                RuntimeConfiguration!.DetermineGlobalSettings();
                if (!string.IsNullOrWhiteSpace(_runtimeConfigPath?.CONNSTRING))
                {
                    RuntimeConfiguration!.ConnectionString = _runtimeConfigPath.CONNSTRING;
                }

                _logger.LogInformation($"Runtime configuration has been successfully loaded.");

                return true;
            }

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
        public string? GetRuntimeConfigJsonString(string? configFileName)
        {
            string? runtimeConfigJson;
            if (!string.IsNullOrEmpty(configFileName))
            {
                if (File.Exists(configFileName))
                {
                    _logger.LogInformation($"Using file {configFileName} to configure the runtime.");
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
                throw new ArgumentNullException($"Could not determine a configuration file name that exists.");
            }

            return runtimeConfigJson;
        }

        /// <summary>
        /// Initialize the runtime configuration provider with the specified configurations.
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
                throw new DataGatewayException(
                    message: "Runtime config isn't setup.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.ErrorInInitialization);
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
    }
}
