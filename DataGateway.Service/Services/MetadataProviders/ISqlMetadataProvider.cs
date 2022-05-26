using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Parsers;
using Azure.DataGateway.Service.Resolvers;

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
        /// Obtains the underlying source object's schema name (SQL) or container name (Cosmos).
        /// </summary>
        string GetSchemaName(string entityName);

        /// <summary>
        /// Obtains the underlying source object's name (SQL table or Cosmos container).
        /// </summary>
        string GetDatabaseObjectName(string entityName);

        /// <summary>
        /// Obtains the underlying TableDefinition for the given entity name.
        /// </summary>
        TableDefinition GetTableDefinition(string entityName);

        Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; }

        FilterParser GetODataFilterParser();

        DatabaseType GetDatabaseType();

        IQueryBuilder GetQueryBuilder();
    }
}
