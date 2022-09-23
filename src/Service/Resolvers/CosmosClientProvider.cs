using System;
using Azure.Core;
using System.Threading.Tasks;
using System.Threading;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using System.Data.Common;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class CosmosClientProvider
    {
        private string? _connectionString;
        private string? _accountEndpoint;
        private string? _accountKey;
        private string? _accessToken;

        public CosmosClient? Client { get; private set; }
        public CosmosClientProvider(RuntimeConfigProvider runtimeConfigProvider)
        {
            // This access token is coming from ConfigurationController parameter, that's why it's not in RuntimeConfig file.
            // On engine first start-up, access token will be null since ConfigurationController hasn't been called at that time.
            _accessToken = runtimeConfigProvider.ManagedIdentityAccessToken;

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
                ParseCosmosConnectionString();

                if (!string.IsNullOrEmpty(_accountKey))
                {
                    Client = new CosmosClientBuilder(_connectionString).WithContentResponseOnWrite(true).Build();
                }
                else if (string.IsNullOrEmpty(_accessToken))
                {
                    Client = new CosmosClient(_accountEndpoint, new DefaultAzureCredential());
                }
                else
                {
                    TokenCredential servicePrincipal = new AADTokenCredential(_accessToken);
                    Client = new CosmosClient(_accountEndpoint, servicePrincipal);
                }
            }
        }

        private class AADTokenCredential : ManagedIdentityCredential
        {
            public string AADToken { get; set; }

            public AADTokenCredential(string aadToken)
            {
                AADToken = aadToken;
            }

            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                return new AccessToken(AADToken, DateTimeOffset.Now.Add(TimeSpan.FromHours(2)));
            }

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
            }
        }

        private void ParseCosmosConnectionString()
        {
            DbConnectionStringBuilder dbConnectionStringBuilder = new()
            {
                ConnectionString = _connectionString
            };
            _accountEndpoint = (string)dbConnectionStringBuilder["AccountEndpoint"];
            _accountKey = (string)dbConnectionStringBuilder["AccountKey"];
        }
    }
}
