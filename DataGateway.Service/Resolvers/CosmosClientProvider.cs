using System;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosClientProvider
    {
        private string? _connectionString;
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
            if (configuration.DatabaseType != DatabaseType.cosmos)
            {
                throw new InvalidOperationException("We shouldn't need a CosmosClientProvider if we're not accessing a CosmosDb");
            }

            if (string.IsNullOrEmpty(_connectionString) || configuration.DatabaseConnection.ConnectionString != _connectionString)
            {
                _connectionString = configuration.DatabaseConnection.ConnectionString;
                Client = new CosmosClientBuilder(configuration.DatabaseConnection.ConnectionString).WithContentResponseOnWrite(true).Build();
            }
        }
    }
}
