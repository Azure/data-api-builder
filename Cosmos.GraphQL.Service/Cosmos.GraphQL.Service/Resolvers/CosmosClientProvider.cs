using Cosmos.GraphQL.Service.configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Options;

namespace Cosmos.GraphQL.Service.Resolvers
{
    public class CosmosClientProvider
    {
        private static CosmosClient _cosmosClient;

        public CosmosClientProvider(IOptions<DataGatewayConfig> dataGatewayConfig)
        {
            _cosmosClient = new CosmosClientBuilder(dataGatewayConfig.Value.DatabaseConnection.ConnectionString).WithContentResponseOnWrite(true).Build();
        }

        public CosmosClient GetClient()
        {
            return _cosmosClient;
        }
    }
}
