using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
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
        Task InitializeAsync();

        /// <summary>
        /// Obtains the underlying source object's schema name (SQL) or container name (Cosmos).
        /// </summary>
        string GetSchemaName(string entityName);

        bool VerifyForeignKeyExistsInDB(
            DatabaseTable databaseObjectA,
            DatabaseTable databaseObjectB);

        /// <summary>
        /// Obtains the underlying source object's name (SQL table or Cosmos container).
        /// </summary>
        string GetDatabaseObjectName(string entityName);

        (string, string) ParseSchemaAndDbTableName(string source);

        /// <summary>
        /// Obtains the underlying SourceDefinition for the given entity name.
        /// </summary>
        SourceDefinition GetSourceDefinition(string entityName);

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
        /// Given the exposed graphQL query or mutation name, Returns true if it belongs to a
        /// stored procedure.
        /// </summary>
        bool IsStoreProcedureQueryOrMutation(string exposedGraphQLQueryOrMutationName);

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
        /// Returns a dictionary of (EntityName, DatabaseObject).
        /// </summary>
        /// <returns></returns>
        public IDictionary<string, DatabaseObject> GetEntityNamesAndDbObjects();

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

        /// <summary>
        /// The GraphQL type is expected to either match the top level entity name from runtime config or the name specified in the singular property 
        /// The entities dictionary should always be using the top level entity name
        /// First try to check if the GraphQL type is matching any key in the entities dictionary
        /// If no match found, then use the GraphQL singular type in the runtime config to look up the top-level entity name from a GraphQLSingularTypeToEntityNameMap
        /// </summary>
        public string GetEntityName(string graphQLType);

        /// <summary>
        /// For the given graphql type, returns the inferred database object.
        /// Does this by first looking up the entity name from the graphql type to entity name mapping.
        /// Subsequently, looks up the database object inferred for the corresponding entity.
        /// </summary>
        /// <param name="graphqlType">Name of the graphql type</param>
        /// <returns>Underlying inferred DatabaseObject.</returns>
        /// <exception cref="DataApiBuilderException">Thrown if entity is not found.</exception>
        public DatabaseObject GetDatabaseObjectForGraphQLType(string graphqlType)
        {
            string entityName = GetEntityName(graphqlType);

            if (!EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? databaseObject))
            {
                throw new DataApiBuilderException(message: $"Source Definition for {entityName} has not been inferred.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
            }

            return databaseObject;
        }
    }
}
