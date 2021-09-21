using Cosmos.GraphQL.Service.configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;

namespace Cosmos.GraphQL.Service.Resolvers
{
    public class CosmosClientProvider
    {
        private static CosmosClient _cosmosClient;
        private static readonly object syncLock = new object();

        private static void init()
        {
            var connectionString = ConfigurationProvider.getInstance().ConnectionString;
            _cosmosClient = new CosmosClientBuilder(connectionString).WithContentResponseOnWrite(true).Build();
        }

        public CosmosClient GetClient()
        {
            return getCosmosClient();
        }

        public CosmosClient getCosmosClient()
        {
            if (_cosmosClient == null)
            {
                lock (syncLock)
                {
                    if (_cosmosClient == null)
                    {
                        init();
                    }
                }
            }
            
            return _cosmosClient;
        }
    }
}
