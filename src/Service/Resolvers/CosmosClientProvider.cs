using System;
<<<<<<< Updated upstream:src/Service/Resolvers/CosmosClientProvider.cs
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
=======
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.Identity;
>>>>>>> Stashed changes:DataGateway.Service/Resolvers/CosmosClientProvider.cs
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
                // TODO: await this..
                InitializeClient(runtimeConfig);
            }
            else
            {
                runtimeConfigProvider.RuntimeConfigLoaded += (sender, newValue) =>
                {
                    // TODO: await this..
                    InitializeClient(newValue);
                };
            }
        }

        private async Task InitializeClient(RuntimeConfig? configuration)
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
                if (string.IsNullOrEmpty(configuration.AccessToken))
                {
                    Client = new CosmosClientBuilder(configuration.ConnectionString).WithContentResponseOnWrite(true).Build();
                }
                else
                {
                    CosmosClientOptions options = new()
                    {
                        EnableContentResponseOnWrite = true
                    };

                    Client = await CosmosClient.CreateAndInitializeAsync(configuration.ConnectionString, new AadTokenCredential(configuration.AccessToken), new List<(string, string)>(), options);
                }
            }
        }
        private class AadTokenCredential : ManagedIdentityCredential
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
