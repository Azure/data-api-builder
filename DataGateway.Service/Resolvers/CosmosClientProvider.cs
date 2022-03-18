using System;
using Azure.DataGateway.Service.Configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosClientProvider
    {
        public CosmosClient? Client { get; private set; }
        public CosmosClientProvider(IOptionsMonitor<DataGatewayConfig> dataGatewayConfig)
        {
            dataGatewayConfig.OnChange((newValue) =>
            {
                InitializeClient(newValue);
            });

            if (dataGatewayConfig.CurrentValue is not null)
            {
                InitializeClient(dataGatewayConfig.CurrentValue);
            }
        }

        private void InitializeClient(DataGatewayConfig configuration)
        {
            if (configuration.DatabaseType != DatabaseType.Cosmos)
            {
                throw new InvalidOperationException("We shouldn't need a CosmosClientProvider if we're not accessing a CosmosDb");
            }

            Client = new CosmosClientBuilder(configuration.DatabaseConnection.ConnectionString).WithContentResponseOnWrite(true).Build();
        }
    }
}
