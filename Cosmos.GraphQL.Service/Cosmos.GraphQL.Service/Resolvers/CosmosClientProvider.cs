using Cosmos.GraphQL.Service.configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;

namespace Cosmos.GraphQL.Service.Resolvers
{
    public class CosmosClientProvider
    {
        private static CosmosClient _cosmosClient;

        public CosmosClientProvider(DatabaseConnection databaseConnection)
        {
            _cosmosClient = new CosmosClientBuilder(databaseConnection.Credentials.GetConnectionString()).WithContentResponseOnWrite(true).Build();
        }

        public CosmosClient GetClient()
        {
            return _cosmosClient;
        }
    }
}
