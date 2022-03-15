using System;
using Azure.DataGateway.Service.Configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosClientProvider
    {
        public CosmosClientProvider(IOptionsMonitor<DataGatewayConfig> dataGatewayConfig)
        {
            dataGatewayConfig.OnChange((newValue) =>
            {
                if (newValue.DatabaseType != DatabaseType.Cosmos)
                {
                    throw new InvalidOperationException("We shouldn't need a CosmosClientProvider if we're not accessing a CosmosDb");
                }

                Client = new CosmosClientBuilder(dataGatewayConfig.CurrentValue.DatabaseConnection.ConnectionString).WithContentResponseOnWrite(true).Build();
            });

            if (dataGatewayConfig.CurrentValue is not null)
            {
                if (dataGatewayConfig.CurrentValue.DatabaseType != DatabaseType.Cosmos)
                {
                    throw new InvalidOperationException("We shouldn't need a CosmosClientProvider if we're not accessing a CosmosDb");
                }

                Client = new CosmosClientBuilder(dataGatewayConfig.CurrentValue.DatabaseConnection.ConnectionString).WithContentResponseOnWrite(true).Build();
            }
        }

        public CosmosClient? Client { get; private set; }
    }
}
