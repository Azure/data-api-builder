using System;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// This class provides access to the runtime configuration and provides a change notification
    /// in the case where the runtime is started without the configuration so it can be set later.
    /// </summary>
    public class RuntimeConfigProvider
    {
        public event EventHandler<RuntimeConfig>? RuntimeConfigLoaded;

        protected virtual RuntimeConfig? RuntimeConfiguration { get; set; }

        public RuntimeConfigProvider() { }

        public RuntimeConfigProvider(IOptions<RuntimeConfigPath>? runtimeConfigPath)
        {
            if (runtimeConfigPath != null)
            {
                RuntimeConfiguration = runtimeConfigPath.Value.LoadRuntimeConfigValue();
            }
        }

        public void Initialize(string configuration, string schema, string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException($"'{nameof(connectionString)}' cannot be null or empty.", nameof(connectionString));
            }

            if (string.IsNullOrEmpty(configuration))
            {
                throw new ArgumentException($"'{nameof(configuration)}' cannot be null or empty.", nameof(configuration));
            }

            if (string.IsNullOrEmpty(schema))
            {
                throw new ArgumentException($"'{nameof(schema)}' cannot be null or empty.", nameof(schema));
            }

            RuntimeConfiguration = RuntimeConfig.GetDeserializedConfig<RuntimeConfig>(configuration);
            RuntimeConfiguration.DetermineGlobalSettings();
            RuntimeConfiguration.ConnectionString = connectionString;
            RuntimeConfiguration = RuntimeConfiguration with { Schema = schema };

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
    }
}
