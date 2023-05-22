// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class CosmosClientProvider
    {
        private string? _connectionString;
        private string? _accountEndpoint;
        private string? _accountKey;
        private readonly string? _accessToken;
        public const string DAB_APP_NAME_ENV = "DAB_APP_NAME_ENV";
        public static readonly string DEFAULT_APP_NAME = $"dab_oss_{Utils.GetProductVersion()}";

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

            if (configuration.DatabaseType is not DatabaseType.cosmosdb_nosql)
            {
                throw new InvalidOperationException("We shouldn't need a CosmosClientProvider if we're not accessing a CosmosDb");
            }

            if (string.IsNullOrEmpty(_connectionString) || configuration.ConnectionString != _connectionString)
            {
                string userAgent = GetCosmosUserAgent();
                CosmosClientOptions options = new()
                {
                    ApplicationName = userAgent
                };

                _connectionString = configuration.ConnectionString;
                ParseCosmosConnectionString();

                if (!string.IsNullOrEmpty(_accountKey))
                {
                    Client = new CosmosClientBuilder(_connectionString).WithContentResponseOnWrite(true)
                        .WithApplicationName(userAgent)
                        .Build();
                }
                else if (string.IsNullOrEmpty(_accessToken))
                {
                    Client = new CosmosClient(_accountEndpoint, new DefaultAzureCredential(), options);
                }
                else
                {
                    TokenCredential servicePrincipal = new AADTokenCredential(_accessToken);
                    Client = new CosmosClient(_accountEndpoint, servicePrincipal, options);
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

        private void ParseCosmosConnectionString()
        {
            DbConnectionStringBuilder dbConnectionStringBuilder = new()
            {
                ConnectionString = _connectionString
            };

            _accountEndpoint = dbConnectionStringBuilder.ContainsKey("AccountEndpoint") ? (string)dbConnectionStringBuilder["AccountEndpoint"] : null;
            _accountKey = dbConnectionStringBuilder.ContainsKey("AccountKey") ? (string)dbConnectionStringBuilder["AccountKey"] : null;
        }

        private static string GetCosmosUserAgent()
        {
            return Environment.GetEnvironmentVariable(DAB_APP_NAME_ENV) ?? DEFAULT_APP_NAME;
        }
    }
}
