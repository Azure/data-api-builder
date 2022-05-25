using System;
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

        /// <summary>
        /// For the entity that is provided as an argument,
        /// get the underlying exposed name associated
        /// with the provided field parameter.
        /// </summary>
        /// <param name="entityName">The entity whose mapping we lookup.</param>
        /// <param name="field">The field used for the lookup in the mapping.</param>
        /// <returns>The exposed name associated with the provided field.</returns>
        string GetExposedColumnName(string entityName, string field);

        /// <summary>
        /// For the entity that is provided as an argument,
        /// get the underlying backing column name associated
        /// with the provided field parameter.
        /// </summary>
        /// <param name="entityName">The entity whose mapping we lookup.</param>
        /// <param name="field">The field used for the lookup in the mapping.</param>
        /// <returns>The backing column associated with the provided field.</returns>
        string GetBackingColumn(string entityName, string field);

        /// <summary>
        /// For the entity that is provided as an argument,
        /// try to get the underlying exposed name associated
        /// with the provided field, if it exists, save in out
        /// parameter, and return true, otherwise return false.
        /// </summary>
        /// <param name="entityName">The entity whose mapping we lookup.</param>
        /// <param name="field">The field used for the lookup in the mapping.</param>
        /// <param name="name">Out parameter in which we will save exposed name.</param>
        /// <returns>True if exists, false otherwise.</returns>
        bool TryGetExposedColumnName(string entityName, string field, out string? name);

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
    }
}
