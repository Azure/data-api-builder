using System;
using System.IO;
using Azure.DataGateway.Config;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// Provides functionality to read the runtime configuration
    /// and does all the necessary steps for bootstrapping the runtime.
    /// </summary>
    public class RuntimeConfigProvider : IRuntimeConfigProvider
    {
        protected RuntimeConfig RuntimeConfig { get; init; }

        public DatabaseType CloudDbType { get; init; }

        public RuntimeConfigProvider(string runtimeConfigJson)
        {
            RuntimeConfig =
                DataGatewayConfig.GetDeserializedConfig<RuntimeConfig>(runtimeConfigJson);

            RuntimeConfig.SetDefaults();
        }

        public RuntimeConfigProvider(
            IOptions<DataGatewayConfig> dataGatewayConfig)
        {
            DataGatewayConfig config = dataGatewayConfig.Value;
            string? runtimeConfigJson = null;
            if (!string.IsNullOrEmpty(config.RuntimeConfigFile))
            {
                runtimeConfigJson = File.ReadAllText(config.RuntimeConfigFile);
            }

            if (string.IsNullOrEmpty(runtimeConfigJson))
            {
                throw new ArgumentNullException("dataGatewayConfig.RuntimeConfig",
                    "The runtime config should be set via ResolverConfigFile.");
            }

            RuntimeConfig =
                DataGatewayConfig.GetDeserializedConfig<RuntimeConfig>(runtimeConfigJson);

            RuntimeConfig.SetDefaults();
        }

        public RuntimeConfig GetRuntimeConfig()
        {
            return RuntimeConfig;
        }
    }
}
