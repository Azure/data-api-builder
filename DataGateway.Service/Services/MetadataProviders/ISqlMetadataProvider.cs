using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Parsers;

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
        /// Obtains the underlying source object's schema name.
        /// </summary>
        string GetSchemaName(string entityName);

        /// <summary>
        /// Obtains the underlying source object's name.
        /// </summary>
        string GetDatabaseObjectName(string entityName);

        /// <summary>
        /// Obtains the underlying TableDefinition for the given entity name.
        /// </summary>
        TableDefinition GetTableDefinition(string entityName);

        FilterParser ODataFilterParser { get; }

        Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; }

        DatabaseType GetDatabaseType();
    }
}
