using System.Collections.Generic;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Services;

namespace Cosmos.GraphQL.Service.Tests
{
    public class MetadataStoreProviderForTest : IMetadataStoreProvider
    {
        private string _graphQLSchema;
        private IDictionary<string, MutationResolver> _mutationResolvers = new Dictionary<string, MutationResolver>();
        private IDictionary<string, GraphQLQueryResolver> _queryResolvers = new Dictionary<string, GraphQLQueryResolver>();

        public void StoreGraphQLSchema(string schema)
        {
            _graphQLSchema = schema;
        }

        public string GetGraphQLSchema()
        {
            return _graphQLSchema;
        }

        public MutationResolver GetMutationResolver(string name)
        {
            MutationResolver result;
            _mutationResolvers.TryGetValue(name, out result);
            return result;
        }

        public GraphQLQueryResolver GetQueryResolver(string name)
        {
            GraphQLQueryResolver result;
            _queryResolvers.TryGetValue(name, out result);
            return result;
        }

        public void StoreMutationResolver(MutationResolver mutationResolver)
        {
            _mutationResolvers.Add(mutationResolver.id, mutationResolver);
        }

        public void StoreQueryResolver(GraphQLQueryResolver queryResolver)
        {
            _queryResolvers.Add(queryResolver.id, queryResolver);
        }
    }
}
