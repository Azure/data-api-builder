using System;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// This class provides access to the runtime configuration and provides a change notification
    /// in the case where the runtime is started without the configuration so it can be set later.
    /// </summary>
    public class RuntimeConfigProvider
    {
        private readonly ILogger<RuntimeConfigPath>? _logger;

        public event EventHandler<RuntimeConfig>? RuntimeConfigLoaded;

        protected virtual RuntimeConfig? RuntimeConfiguration { get; private set; }

        public virtual string RestPath
        {
            get { return RuntimeConfiguration is not null ? RuntimeConfiguration.RestGlobalSettings.Path : string.Empty; }
        }

        public RuntimeConfigProvider(
            IOptions<RuntimeConfigPath>? runtimeConfigPath,
            ILogger<RuntimeConfigPath> logger)
        {
            if (runtimeConfigPath != null)
            {
                if (runtimeConfigPath.Value.TryLoadRuntimeConfigValue())
                {
                    RuntimeConfiguration = RuntimeConfigPath.LoadedRuntimeConfig;
                }
            }

            _logger = logger;
        }

        public RuntimeConfigProvider() { }

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
                    out RuntimeConfig? runtimeConfig,
                    _logger))
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
