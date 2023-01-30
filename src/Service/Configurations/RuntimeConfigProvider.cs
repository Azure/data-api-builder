using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
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
        public delegate Task<bool> RuntimeConfigLoadedHandler(RuntimeConfigProvider sender, RuntimeConfig config);

        public List<RuntimeConfigLoadedHandler> RuntimeConfigLoadedHandlers { get; } = new List<RuntimeConfigLoadedHandler>();

        /// <summary>
        /// The config provider logger is a static member because we use it in static methods
        /// like LoadRuntimeConfigValue, GetRuntimeConfigJsonString which themselves are static
        /// to be used by tests.
        /// </summary>
        public static ILogger<RuntimeConfigProvider>? ConfigProviderLogger;

        /// <summary>
        /// Represents the path to the runtime configuration file.
        /// </summary>
        public RuntimeConfigPath? RuntimeConfigPath { get; private set; }

        /// <summary>
        /// Represents the loaded and deserialized runtime configuration.
        /// </summary>
        protected virtual RuntimeConfig? RuntimeConfiguration { get; private set; }

        public virtual string RestPath
        {
            get { return RuntimeConfiguration is not null ? RuntimeConfiguration.RestGlobalSettings.Path : string.Empty; }
        }

        /// <summary>
        /// The access token representing a Managed Identity to connect to the database.
        /// </summary>
        public string? ManagedIdentityAccessToken { get; private set; }

        /// <summary>
        /// Specifies whether configuration was provided late.
        /// </summary>
        public bool IsLateConfigured { get; set; }

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
                RuntimeConfig.TryGetDeserializedRuntimeConfig(
                    runtimeConfigJson,
                    out runtimeConfig,
                    ConfigProviderLogger))
            {
                runtimeConfig!.MapGraphQLSingularTypeToEntityName(ConfigProviderLogger);
                if (!string.IsNullOrWhiteSpace(configPath?.CONNSTRING))
                {
                    runtimeConfig!.ConnectionString = configPath.CONNSTRING;
                }

                if (ConfigProviderLogger is not null)
                {
                    ConfigProviderLogger.LogInformation($"Runtime configuration has been successfully loaded.");
                    if (runtimeConfig.GraphQLGlobalSettings.Enabled)
                    {
                        ConfigProviderLogger.LogInformation($"GraphQL path: {runtimeConfig.GraphQLGlobalSettings.Path}");
                    }
                    else
                    {
                        ConfigProviderLogger.LogInformation($"GraphQL is disabled.");
                    }

                    if (runtimeConfig.AuthNConfig is not null)
                    {
                        ConfigProviderLogger.LogInformation($"{runtimeConfig.AuthNConfig.Provider}");
                    }
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
        /// <param name="accessToken">The string representation of a managed identity access token
        /// <param name="Database"> The name of the database to be used for Cosmos</param>
        /// useful to connect to the database.</param>
        public async Task<bool> Initialize(
            string configuration,
            string? schema,
            string connectionString,
            string? accessToken,
            string? database = null)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException($"'{nameof(connectionString)}' cannot be null or empty.", nameof(connectionString));
            }

            if (string.IsNullOrEmpty(configuration))
            {
                throw new ArgumentException($"'{nameof(configuration)}' cannot be null or empty.", nameof(configuration));
            }

            if (RuntimeConfig.TryGetDeserializedRuntimeConfig(
                    configuration,
                    out RuntimeConfig? runtimeConfig,
                    ConfigProviderLogger!))
            {
                RuntimeConfiguration = runtimeConfig;
                RuntimeConfiguration!.MapGraphQLSingularTypeToEntityName(ConfigProviderLogger);
                RuntimeConfiguration!.ConnectionString = connectionString;

                if (RuntimeConfiguration!.DatabaseType == DatabaseType.cosmosdb_nosql)
                {
                    if (string.IsNullOrEmpty(schema))
                    {
                        throw new ArgumentException($"'{nameof(schema)}' cannot be null or empty.", nameof(schema));
                    }

                    CosmosDbNoSqlOptions? cosmosDb = RuntimeConfiguration.DataSource.CosmosDbNoSql! with { GraphQLSchema = schema };

                    if (!string.IsNullOrEmpty(database))
                    {
                        cosmosDb = cosmosDb with { Database = database };
                    }

                    DataSource dataSource = RuntimeConfiguration.DataSource with { CosmosDbNoSql = cosmosDb };
                    RuntimeConfiguration = RuntimeConfiguration with { DataSource = dataSource };
                }
            }

            ManagedIdentityAccessToken = accessToken;

            List<Task<bool>> configLoadedTasks = new();
            if (RuntimeConfiguration is not null)
            {
                foreach (RuntimeConfigLoadedHandler configLoadedHandler in RuntimeConfigLoadedHandlers)
                {
                    configLoadedTasks.Add(configLoadedHandler(this, RuntimeConfiguration));
                }
            }

            await Task.WhenAll(configLoadedTasks);

            IsLateConfigured = true;

            // Verify that all tasks succeeded. 
            return configLoadedTasks.All(x => x.Result);
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

        /// <summary>
        /// Attempt to acquire runtime configuration metadata.
        /// </summary>
        /// <param name="runtimeConfig">Populated runtime configuartion, if present.</param>
        /// <returns>True when runtime config is provided, otherwise false.</returns>
        public virtual bool TryGetRuntimeConfiguration([NotNullWhen(true)] out RuntimeConfig? runtimeConfig)
        {
            runtimeConfig = RuntimeConfiguration;
            return RuntimeConfiguration is not null;
        }

        public virtual bool IsDeveloperMode()
        {
            return RuntimeConfiguration?.HostGlobalSettings.Mode is HostModeType.Development;
        }

        /// <summary>
        /// Return whether to allow GraphQL introspection using runtime configuration metadata.
        /// </summary>
        /// <returns>True if introspection is allowed, otherwise false.</returns>
        public virtual bool IsIntrospectionAllowed()
        {
            return RuntimeConfiguration is not null && RuntimeConfiguration.GraphQLGlobalSettings.AllowIntrospection;
        }
    }
}
