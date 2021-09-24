using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Cosmos.GraphQL.Service.Models;
using Cosmos.GraphQL.Services;

namespace Cosmos.GraphQL.Service.Tests
{
    public class MetadataStoreProviderForTest : IMetadataStoreProvider
    {

        /// <summary>
        /// String Representation of graphQL schema, non escaped.
        /// </summary>
        private string _graphQLSchema;
        private IDictionary<string, MutationResolver> _mutationResolver = new Dictionary<string, MutationResolver>();
        private IDictionary<string, GraphQLQueryResolver> _queryResolver = new Dictionary<string, GraphQLQueryResolver>();

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
            _mutationResolver.TryGetValue(name, out result);
            return result;
        }

        public GraphQLQueryResolver GetQueryResolver(string name)
        {
            GraphQLQueryResolver result;
            _queryResolver.TryGetValue(name, out result);
            return result;
        }

        public void StoreMutationResolver(MutationResolver mutationResolver)
        {
            _mutationResolver.Add(mutationResolver.id, mutationResolver);
        }

        public void StoreQueryResolver(GraphQLQueryResolver queryResolver)
        {
            _queryResolver.Add(queryResolver.id, queryResolver);
        }
    }
}
