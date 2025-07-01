// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using Azure.Core;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Product;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    public class CosmosClientProvider
    {
        private readonly Dictionary<string, string?> _accessToken;

        public Dictionary<string, CosmosClient?> Clients { get; private set; }

        public RuntimeConfigProvider RuntimeConfigProvider;

        public CosmosClientProvider(RuntimeConfigProvider runtimeConfigProvider)
        {
            // This access token is coming from ConfigurationController parameter, that's why it's not in RuntimeConfig file.
            // On engine first start-up, access token will be null since ConfigurationController hasn't been called at that time.
            _accessToken = runtimeConfigProvider.ManagedIdentityAccessToken;
            Clients = new Dictionary<string, CosmosClient?>();
            RuntimeConfigProvider = runtimeConfigProvider;

            if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                InitializeClient(runtimeConfig);
            }
            else
            {
                runtimeConfigProvider.RuntimeConfigLoadedHandlers.Add((sender, newValue) =>
                {
                    InitializeClient(newValue);
                    return Task.FromResult(true);
                });
            }
        }

        private void InitializeClient(RuntimeConfig? configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration),
                    "Cannot initialize a CosmosClientProvider without the runtime config.");
            }

            if (!configuration.ListAllDataSources().Any(x => x.DatabaseType is DatabaseType.CosmosDB_NoSQL))
            {
                return;
            }

            IEnumerable<KeyValuePair<string, DataSource>> cosmosDb = configuration.GetDataSourceNamesToDataSourcesIterator().Where(x => x.Value.DatabaseType == DatabaseType.CosmosDB_NoSQL);

            foreach ((string dataSourceName, DataSource dataSource) in cosmosDb)
            {
                if (!Clients.ContainsKey(dataSourceName))
                {
                    CosmosClient client;
                    string userAgent = ProductInfo.GetDataApiBuilderUserAgent();
                    CosmosClientOptions options = new()
                    {
                        ApplicationName = userAgent
                    };

                    (string? accountEndPoint, string? accountKey) = ParseCosmosConnectionString(dataSource.ConnectionString);

                    if (!string.IsNullOrEmpty(accountKey))
                    {
                        client = new CosmosClientBuilder(dataSource.ConnectionString).WithContentResponseOnWrite(true)
                            .WithApplicationName(userAgent)
                            .Build();
                    }
                    else if (!_accessToken.ContainsKey(dataSourceName))
                    {
                        client = new CosmosClient(accountEndPoint, new DefaultAzureCredential(), options); // CodeQL [SM05137] DefaultAzureCredential will use Managed Identity if available or fallback to default.
                    }
                    else
                    {
                        TokenCredential servicePrincipal = new AADTokenCredential(_accessToken[dataSourceName]!);
                        client = new CosmosClient(accountEndPoint, servicePrincipal, options);
                    }

                    Clients.Add(dataSourceName, client);
                }
            }
        }

        private class AADTokenCredential : ManagedIdentityCredential
        {
            private readonly string _aadToken;

            public AADTokenCredential(string aadToken)
            {
                _aadToken = aadToken;
            }

            // Returns AccessToken which can be used to authenticate service client calls
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                try
                {
                    JwtSecurityTokenHandler handler = new();
                    JwtSecurityToken token = handler.ReadJwtToken(_aadToken);
                    return new AccessToken(_aadToken, new DateTimeOffset(token.ValidTo));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Managed Identity Access Token is invalid." + ex.Message);
                }
            }

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
            }
        }

        private static (string?, string?) ParseCosmosConnectionString(string connectionString)
        {
            DbConnectionStringBuilder dbConnectionStringBuilder = new()
            {
                ConnectionString = connectionString
            };

            string? accountEndpoint = dbConnectionStringBuilder.ContainsKey("AccountEndpoint") ? (string)dbConnectionStringBuilder["AccountEndpoint"] : null;
            string? accountKey = dbConnectionStringBuilder.ContainsKey("AccountKey") ? (string)dbConnectionStringBuilder["AccountKey"] : null;

            return (accountEndpoint, accountKey);
        }

    }
}
