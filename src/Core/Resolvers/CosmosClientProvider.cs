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
        private string? _accountEndpoint;
        private string? _accountKey;
        private readonly Dictionary<string, string?> _accessToken;
        public string _defaultDataSourceName = "";

        public Dictionary<string, CosmosClient?> Clients { get; private set; }

        public CosmosClientProvider(RuntimeConfigProvider runtimeConfigProvider)
        {
            // This access token is coming from ConfigurationController parameter, that's why it's not in RuntimeConfig file.
            // On engine first start-up, access token will be null since ConfigurationController hasn't been called at that time.
            _accessToken = runtimeConfigProvider.ManagedIdentityAccessToken;
            Clients = new Dictionary<string, CosmosClient?>();

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

            if (!configuration.DatasourceNameToDataSource.Values.Any(x => x.DatabaseType is DatabaseType.CosmosDB_NoSQL))
            {
                throw new InvalidOperationException("We shouldn't need a CosmosClientProvider if we're not accessing a CosmosDb");
            }

            IEnumerable<KeyValuePair<string, DataSource>> cosmosDb = configuration.DatasourceNameToDataSource.Where(x => x.Value.DatabaseType == DatabaseType.CosmosDB_NoSQL);

            foreach (KeyValuePair<string, DataSource> dataSourcePair in cosmosDb)
            {
                string dataSourceName = dataSourcePair.Key;
                DataSource dataSource = dataSourcePair.Value;
                if (!Clients.ContainsKey(dataSourceName))
                {
                    CosmosClient client;
                    string userAgent = ProductInfo.GetDataApiBuilderUserAgent();
                    CosmosClientOptions options = new()
                    {
                        ApplicationName = userAgent
                    };

                    ParseCosmosConnectionString(dataSource.ConnectionString);

                    if (!string.IsNullOrEmpty(_accountKey))
                    {
                        client = new CosmosClientBuilder(dataSource.ConnectionString).WithContentResponseOnWrite(true)
                            .WithApplicationName(userAgent)
                            .Build();
                    }
                    else if (_accessToken.ContainsKey(dataSourceName))
                    {
                        client = new CosmosClient(_accountEndpoint, new DefaultAzureCredential(), options);
                    }
                    else
                    {
                        TokenCredential servicePrincipal = new AADTokenCredential(_accessToken[dataSourceName]!);
                        client = new CosmosClient(_accountEndpoint, servicePrincipal, options);
                    }

                    Clients.Add(dataSourceName, client);
                }
            }

            _defaultDataSourceName = configuration.DefaultDataSourceName;
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

        private void ParseCosmosConnectionString(string connectionString)
        {
            DbConnectionStringBuilder dbConnectionStringBuilder = new()
            {
                ConnectionString = connectionString
            };

            _accountEndpoint = dbConnectionStringBuilder.ContainsKey("AccountEndpoint") ? (string)dbConnectionStringBuilder["AccountEndpoint"] : null;
            _accountKey = dbConnectionStringBuilder.ContainsKey("AccountKey") ? (string)dbConnectionStringBuilder["AccountKey"] : null;
        }

    }
}
