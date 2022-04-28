using Azure.DataGateway.Config;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// Provides bootstrap of the runtime configuration.
    /// </summary>
    public interface IRuntimeConfigProvider
    {
        /// <summary>
        /// Retrieves the runtime config.
        /// </summary>
        RuntimeConfig GetRuntimeConfig();
    }
}
