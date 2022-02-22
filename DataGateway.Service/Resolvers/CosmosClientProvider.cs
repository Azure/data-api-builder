using Azure.DataGateway.Service.Configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosClientProvider
    {
        public CosmosClientProvider(IOptions<DataGatewayConfig> dataGatewayConfig)
        {
            Client = new CosmosClientBuilder(dataGatewayConfig.Value.DatabaseConnection.ConnectionString).WithContentResponseOnWrite(true).Build();
        }

        public CosmosClient Client { get; }
    }
}
