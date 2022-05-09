using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    public class MetadataStoreProviderForTest : IGraphQLMetadataProvider
    {
        public string GraphQLSchema { get; set; }
        public Dictionary<string, MutationResolver> MutationResolvers { get; set; } = new();
        public Dictionary<string, TableDefinition> Tables { get; set; } = new();
        public Dictionary<string, GraphQLType> GraphQLTypes { get; set; } = new();

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

        public void RemoveMutationResolver(string mutationResolverId)
        {
            if (MutationResolvers.ContainsKey(mutationResolverId))
            {
                MutationResolvers.Remove(mutationResolverId);
            }
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
            throw new System.NotImplementedException();
        }

        public static Task InitializeAsync()
        {
            // no-op
            return Task.CompletedTask;
        }
    }
}
