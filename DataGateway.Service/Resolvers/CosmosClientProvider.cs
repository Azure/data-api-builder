using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosClientProvider
    {
        private string? _connectionString;
        private string? _aadToken;
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

            if (string.IsNullOrEmpty(_aadToken) || configuration.AadToken != _aadToken)
            {
                _aadToken = configuration.AadToken;
                TokenCredential tokenCredential = new AadTokenCredential(configuration.AadToken);
                Client = new CosmosClientBuilder(configuration.AccountEndpoint, tokenCredential).WithContentResponseOnWrite(true).Build();
                //Client = new CosmosClientBuilder(configuration.ConnectionString).WithContentResponseOnWrite(true).Build();
            }
        }

        private class AadTokenCredential : TokenCredential
        {
            public string AadToken { get; set; }

            public AadTokenCredential(string aadToken)
            {
                AadToken = aadToken;
            }

            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                return new AccessToken(AadToken, DateTimeOffset.Now.Add(TimeSpan.FromHours(2)));
            }

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
            }
        }
    }
}
