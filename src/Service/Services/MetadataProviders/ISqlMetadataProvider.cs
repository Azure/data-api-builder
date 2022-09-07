using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Parsers;
using Azure.DataApiBuilder.Service.Resolvers;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// Interface to retrieve information for the runtime from the database.
    /// </summary>
    public interface ISqlMetadataProvider
    {
        /// <summary>
        /// Initializes this metadata provider for the runtime.
        /// </summary>
        Task InitializeAsync(RuntimeConfigProvider runtimeConfigProvider);

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

        /// <summary>
        /// Obtains the underlying StoredProcedureDefinition for the given entity name.
        /// </summary>
        StoredProcedureDefinition GetStoredProcedureDefinition(string entityName);

        Dictionary<string, DatabaseObject> EntityToDatabaseObject { get; set; }

        /// <summary>
        /// Obtains the underlying OData parser.
        /// </summary>
        /// <returns></returns>
        ODataParser GetODataParser();

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
        /// Try to obtain the name of the Entity that has the provided Path. If It
        /// exists save in out param, and return true, otherwise return false.
        /// </summary>
        /// <param name="entityPathName">Entity's path as seen in a request.</param>
        /// <param name="entityName">Name of the associated entity.</param>
        /// <returns>True if exists, otherwise false.</returns>
        bool TryGetEntityNameFromPath(string entityPathName, [NotNullWhen(true)] out string? entityName);

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

        /// <summary>
        /// Gets Partition Key Path of a database container.
        /// </summary>
        string? GetPartitionKeyPath(string database, string container);

        /// <summary>
        /// Sets Partition Key Path of a database container.
        /// Example of a Partition Key Path looks like: /id
        /// Example of a Parition Key Path on nested inner object: /character/id
        /// When a partition key path is being looked up for the first time, this method will add it to the dictionary
        /// </summary>
        void SetPartitionKeyPath(string database, string container, string partitionKeyPath);
    }
}
