using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// To resolve queries and requests certain GraphQL metadata is necessary. This is
    /// the interface that can be used to get this metadata.
    /// </summary>
    public interface IGraphQLMetadataProvider
    {
        /// <summary>
        /// Initializes this metadata provider for the runtime.
        /// </summary>
        Task InitializeAsync();

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
        /// Gets metadata required to resolve the GraphQL type with the given
        /// name.
        /// </summary>
        GraphQLType GetGraphQLType(string name);

        /// <summary>
        /// Returns the resolved config
        /// </summary>
        ResolverConfig GetResolvedConfig();
    }
}
