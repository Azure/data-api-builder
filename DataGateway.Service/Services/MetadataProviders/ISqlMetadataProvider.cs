using System.Threading.Tasks;
using Azure.DataGateway.Config;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// Interface to retrieve information for the runtime from the database.
    /// </summary>
    public interface ISqlMetadataProvider
    {
        /// <summary>
        /// Initializes this metadata provider for the runtime.
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Obtains the underlying TableDefinition for the given entity name.
        /// </summary>
        public TableDefinition GetTableDefinition(string entityName);

    }
}
