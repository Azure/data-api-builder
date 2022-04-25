using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// Provides bootstrap of the runtime configuration.
    /// </summary>
    public interface IRuntimeConfigProvider
    {
        /// <summary>
        /// Initializes this metadata provider for the runtime.
        /// </summary>
        Task InitializeAsync();
    }
}
