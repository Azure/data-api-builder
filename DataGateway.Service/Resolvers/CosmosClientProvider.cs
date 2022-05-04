using System;
using Azure.DataGateway.Config;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosClientProvider
    {
        private string? _connectionString;
        public CosmosClient? Client { get; private set; }
        public CosmosClientProvider(IOptionsMonitor<RuntimeConfig> runtimeConfig)
        {
            runtimeConfig.OnChange((newValue) =>
            {
                InitializeClient(newValue);
            });

            if (runtimeConfig.CurrentValue is not null)
            {
                InitializeClient(runtimeConfig.CurrentValue);
            }
        }

        private void InitializeClient(RuntimeConfig configuration)
        {
            if (configuration.DatabaseType != DatabaseType.cosmos)
            {
                throw new InvalidOperationException("We shouldn't need a CosmosClientProvider if we're not accessing a CosmosDb");
            }

            if (string.IsNullOrEmpty(_connectionString) || configuration.ConnectionString != _connectionString)
            {
                _connectionString = configuration.ConnectionString;
                Client = new CosmosClientBuilder(configuration.ConnectionString).WithContentResponseOnWrite(true).Build();
            }
        }
    }
}
