using Azure.DataGateway.Service.Configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosClientProvider
    {
        public CosmosClientProvider(IOptionsMonitor<DataGatewayConfig> dataGatewayConfig)
        {
            dataGatewayConfig.OnChange((newValue) =>
            {
                Client = new CosmosClientBuilder(dataGatewayConfig.CurrentValue.DatabaseConnection.ConnectionString).WithContentResponseOnWrite(true).Build();
            });

            if (dataGatewayConfig.CurrentValue is not null)
            {
                Client = new CosmosClientBuilder(dataGatewayConfig.CurrentValue.DatabaseConnection.ConnectionString).WithContentResponseOnWrite(true).Build();
            }
        }

        public CosmosClient? Client { get; private set; }
    }
}
