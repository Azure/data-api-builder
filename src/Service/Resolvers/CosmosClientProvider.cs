using System;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class CosmosClientProvider
    {
        private string? _connectionString;
        public CosmosClient? Client { get; private set; }
        public CosmosClientProvider(RuntimeConfigProvider runtimeConfigProvider)
        {
            if (runtimeConfigProvider.TryGetRuntimeConfiguration(out RuntimeConfig? runtimeConfig))
            {
                InitializeClient(runtimeConfig);
            }
            else
            {
                runtimeConfigProvider.RuntimeConfigLoaded += (sender, newValue) =>
                {
                    InitializeClient(newValue);
                };
            }
        }

        private void InitializeClient(RuntimeConfig? configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration),
                    "Cannot initialize a CosmosClientProvider without the runtime config.");
            }

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
