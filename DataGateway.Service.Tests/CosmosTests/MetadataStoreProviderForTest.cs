using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    public class MetadataStoreProviderForTest : IMetadataStoreProvider
    {
        public string GraphQLSchema { get; set; }
        private readonly FilterParser _filterParser;
        public Dictionary<string, MutationResolver> MutationResolvers { get; set; } = new();
        public Dictionary<string, TableDefinition> Tables { get; set; } = new();
        public Dictionary<string, GraphQLType> GraphQLTypes { get; set; } = new();
        public DatabaseSchema DatabaseSchema { get; set; } = new();

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

        public void StoreGraphQLType(string name, GraphQLType graphQLType)
        {
            GraphQLTypes.Add(name, graphQLType);
        }

        public GraphQLType GetGraphQLType(string name)
        {
            return GraphQLTypes.TryGetValue(name, out GraphQLType graphqlType) ? graphqlType : null;
        }

        public ResolverConfig GetResolvedConfig()
        {
            return new ResolverConfig(GraphQLSchema, string.Empty, DatabaseSchema)
            {
                GraphQLTypes = GraphQLTypes,
                MutationResolvers = MutationResolvers == null ? null : MutationResolvers.Values.ToList(),
            };
        }

        public FilterParser GetFilterParser()
        {
            return _filterParser;
        }
    }
}
