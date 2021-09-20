using Cosmos.GraphQL.Service.Models;

namespace Cosmos.GraphQL.Services
{
    
    public interface IMetadataStoreProvider
    {
        void StoreGraphQLSchema(string schema);
        string GetGraphQLSchema();
        MutationResolver GetMutationResolver(string name);
        GraphQLQueryResolver GetQueryResolver(string name);
        void StoreMutationResolver(MutationResolver mutationResolver);
        void StoreQueryResolver(GraphQLQueryResolver mutationResolver);
    }
}