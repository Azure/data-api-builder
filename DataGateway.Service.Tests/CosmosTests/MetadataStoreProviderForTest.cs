using System.Collections.Generic;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Services;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    public class MetadataStoreProviderForTest : IMetadataStoreProvider
    {
        public string GraphQLSchema { get; set; }
        private readonly FilterParser _filterParser;
        public Dictionary<string, MutationResolver> MutationResolvers { get; set; } = new();
        public Dictionary<string, GraphQLQueryResolver> QueryResolvers { get; set; } = new();
        public Dictionary<string, TableDefinition> Tables { get; set; } = new();
        public Dictionary<string, GraphqlType> GraphqlTypes { get; set; } = new();

        public string GetGraphQLSchema()
        {
            return GraphQLSchema;
        }

        public MutationResolver GetMutationResolver(string name)
        {
            MutationResolver result;
            MutationResolvers.TryGetValue(name, out result);
            return result;
        }

        public GraphQLQueryResolver GetQueryResolver(string name)
        {
            GraphQLQueryResolver result;
            QueryResolvers.TryGetValue(name, out result);
            return result;
        }

        public TableDefinition GetTableDefinition(string name)
        {
            TableDefinition result;
            Tables.TryGetValue(name, out result);
            return result;
        }

        public void StoreMutationResolver(MutationResolver mutationResolver)
        {
            MutationResolvers.Add(mutationResolver.Id, mutationResolver);
        }

        public void StoreQueryResolver(GraphQLQueryResolver queryResolver)
        {
            QueryResolvers.Add(queryResolver.Id, queryResolver);
        }

        public void StoreGraphQLType(string name, GraphqlType graphQLType)
        {
            GraphqlTypes.Add(name, graphQLType);
        }

        public GraphQLType GetGraphQLType(string name)
        {
            return GraphqlTypes.TryGetValue(name, out GraphqlType graphqlType) ? graphqlType : null;
        }

        public ResolverConfig GetResolvedConfig()
        {
            throw new System.NotImplementedException();
        }

        public FilterParser GetFilterParser()
        {
            return _filterParser;
        }
    }
}
