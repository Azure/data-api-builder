using System;
using System.IO;
using System.Threading.Tasks;
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

            CloudDbType = RuntimeConfig.DataSource.DatabaseType;
        }

        public RuntimeConfig GetRuntimeConfig()
        {
            return RuntimeConfig;
        }

        /// <summary>
        /// Does further initialization work that needs to happen
        /// asynchronously and hence not done in the constructor.
        /// </summary>
        public virtual Task InitializeAsync()
        {
            // no-op
            return Task.CompletedTask;
        }
    }
}
