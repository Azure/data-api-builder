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

        /// <summary>
        /// Obtains the underlying OData filter parser.
        /// </summary>
        /// <returns></returns>
        FilterParser GetODataFilterParser();

        /// <summary>
        /// For the entity that is provided as an argument,
        /// try to get the exposed name associated
        /// with the provided field, if it exists, save in out
        /// parameter, and return true, otherwise return false.
        /// </summary>
        /// <param name="entityName">The entity whose mapping we lookup.</param>
        /// <param name="backingFieldName">The field used for the lookup in the mapping.</param>
        /// <param name="name">Out parameter in which we will save exposed name.</param>
        /// <returns>True if exists, false otherwise.</returns>
        bool TryGetExposedColumnName(string entityName, string backingFieldName, out string? name);

        /// <summary>
        /// For the entity that is provided as an argument,
        /// try to get the underlying backing column name associated
        /// with the provided field, if it exists, save in out
        /// parameter, and return true, otherwise return false.
        /// </summary>
        /// <param name="entityName">The entity whose mapping we lookup.</param>
        /// <param name="field">The field used for the lookup in the mapping.</param>
        /// <param name="name"/>Out parameter in which we will save backing column name.<param>
        /// <returns>True if exists, false otherwise.</returns>
        bool TryGetBackingColumn(string entityName, string field, out string? name);

        /// <summary>
        /// Obtains the underlying database type.
        /// </summary>
        /// <returns></returns>
        DatabaseType GetDatabaseType();

        IQueryBuilder GetQueryBuilder();

        /// <summary>
        /// Returns a collection of (EntityName, DatabaseObject) without
        /// exposing the internal representation.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<KeyValuePair<string, DatabaseObject>> GetEntityNamesAndDbObjects();
    }
}
