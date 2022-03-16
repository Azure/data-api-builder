using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// To resolve queries and requests certain metadata is necessary. This is
    /// the interface that can be used to get this metadata.
    /// </summary>
    public interface IMetadataStoreProvider
    {
        /// <summary>
        /// Gets the string version of the GraphQL schema.
        /// </summary>
        string GetGraphQLSchema();
        /// <summary>
        /// Gets metadata required to resolve the GraphQL mutation with the
        /// given name.
        /// </summary>
        MutationResolver GetMutationResolver(string name);

        /// <summary>
        /// Gets the database schema information for the given table.
        /// </summary>
        TableDefinition GetTableDefinition(string name);
        /// <summary>
        /// Gets metadata required to resolve the GraphQL type with the given
        /// name.
        /// </summary>
        GraphQLType GetGraphQLType(string name);

        /// <summary>
        /// Returns the resolved config
        /// </summary>
        ResolverConfig GetResolvedConfig();

        /// <summary>
        /// Returns the Filter Parser
        /// </summary>
        /// <returns></returns>
        FilterParser GetFilterParser();
    }
}
