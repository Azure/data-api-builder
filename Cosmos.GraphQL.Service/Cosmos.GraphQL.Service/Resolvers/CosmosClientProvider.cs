using System.Configuration;
using Cosmos.GraphQL.Service.configurations;
using Microsoft.Azure.Cosmos;

namespace Cosmos.GraphQL.Service.Resolvers
{
    public static class CosmosClientProvider
    {
        
        private static CosmosClient _cosmosClient;
        private static readonly object syncLock = new object();

        private static void init()
        {
            var cred = ConfigurationProvider.getCosmosCredentials();
            _cosmosClient = new CosmosClient(cred.EndpointUrl, cred.AuthorizationKey);
        }
        
        public static CosmosClient getCosmosClient()
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
            
            // assert cred != null
            return _cosmosClient;
        }


        public static Container getCosmosContainer()
        {
            return getCosmosClient().GetDatabase(ConfigurationProvider.getDatabaseName())
                .GetContainer(ConfigurationProvider.getContainer());

        }
    }

}