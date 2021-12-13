using Azure.DataGateway.Service.configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosClientProvider
    {
        private static CosmosClient _cosmosClient;

        public CosmosClientProvider(IOptions<DataGatewayConfig> dataGatewayConfig)
        {
            Init();
            //_cosmosClient = new CosmosClientBuilder(dataGatewayConfig.Value.DatabaseConnection.ConnectionString).WithContentResponseOnWrite(true).Build();
        }

        private static void Init()
        {
            string key = CosmosDbConfiguration.Instance.CosmosKey;
            string endpoint = CosmosDbConfiguration.Instance.CosmosEndpoint;
            if (key != null && endpoint != null)
            {
                _cosmosClient = new CosmosClientBuilder(endpoint, key).WithContentResponseOnWrite(true).Build();
            }
        }

        public CosmosClient Client
        {
            get
            {
                if (_cosmosClient == null)
                {
                    Init();
                }

                return _cosmosClient;
            }
        }
    }
}
