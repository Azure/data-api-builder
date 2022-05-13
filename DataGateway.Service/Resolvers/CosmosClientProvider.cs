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
        public CosmosClientProvider(IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath)
        {
            runtimeConfigPath.OnChange((newValue) =>
            {
                newValue.SetRuntimeConfigValue();
                InitializeClient(runtimeConfigPath.CurrentValue.ConfigValue);
            });

            InitializeClient(runtimeConfigPath.CurrentValue.ConfigValue);
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
