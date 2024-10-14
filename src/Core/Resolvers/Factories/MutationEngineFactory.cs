// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using static Azure.DataApiBuilder.Config.DabConfigEvents;

namespace Azure.DataApiBuilder.Core.Resolvers.Factories
{
    /// <summary>
    /// MutationEngineFactory class.
    /// Used to get the IMutationEngine based on database type.
    /// </summary>
    public class MutationEngineFactory : IMutationEngineFactory
    {
        private Dictionary<DatabaseType, IMutationEngine> _mutationEngines;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly IAbstractQueryManagerFactory _queryManagerFactory;
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly CosmosClientProvider _cosmosClientProvider;
        private readonly IQueryEngineFactory _queryEngineFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAuthorizationResolver _authorizationResolver;
        private readonly GQLFilterParser _gQLFilterParser;

        /// <summary>
        /// Initializes a new instance of the <see cref="MutationEngineFactory"/> class.
        /// </summary>
        /// <param name="runtimeConfigProvider">runtimeConfigProvider.</param>
        /// <param name="queryManagerFactory">queryManagerFactory</param>
        /// <param name="metadataProviderFactory">metadataProviderFactory.</param>
        /// <param name="cosmosClientProvider">cosmosClientProvider</param>
        /// <param name="queryEngineFactory">queryEngineFactory.</param>
        /// <param name="httpContextAccessor">httpContextAccessor.</param>
        /// <param name="authorizationResolver">authorizationResolver.</param>
        /// <param name="gQLFilterParser">GqlFilterParser.</param>
        public MutationEngineFactory(RuntimeConfigProvider runtimeConfigProvider,
            IAbstractQueryManagerFactory queryManagerFactory,
            IMetadataProviderFactory metadataProviderFactory,
            CosmosClientProvider cosmosClientProvider,
            IQueryEngineFactory queryEngineFactory,
            IHttpContextAccessor httpContextAccessor,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            HotReloadEventHandler<HotReloadEventArgs>? handler)

        {
            handler?.Subscribe(MUTATION_ENGINE_FACTORY_ON_CONFIG_CHANGED, OnConfigChanged);
            _cosmosClientProvider = cosmosClientProvider;
            _queryManagerFactory = queryManagerFactory;
            _metadataProviderFactory = metadataProviderFactory;
            _httpContextAccessor = httpContextAccessor;
            _authorizationResolver = authorizationResolver;
            _queryEngineFactory = queryEngineFactory;
            _runtimeConfigProvider = runtimeConfigProvider;
            _gQLFilterParser = gQLFilterParser;
            _mutationEngines = new Dictionary<DatabaseType, IMutationEngine>();
            ConfigureMutationEngines();
        }

        private void ConfigureMutationEngines()
        {
            RuntimeConfig config = _runtimeConfigProvider.GetConfig();

            if (config.SqlDataSourceUsed)
            {
                IMutationEngine mutationEngine = new SqlMutationEngine(
                    _queryManagerFactory,
                    _metadataProviderFactory,
                    _queryEngineFactory,
                    _authorizationResolver,
                    _gQLFilterParser,
                    _httpContextAccessor,
                    _runtimeConfigProvider);
                _mutationEngines.Add(DatabaseType.MySQL, mutationEngine);
                _mutationEngines.Add(DatabaseType.MSSQL, mutationEngine);
                _mutationEngines.Add(DatabaseType.PostgreSQL, mutationEngine);
                _mutationEngines.Add(DatabaseType.DWSQL, mutationEngine);
            }

            if (config.CosmosDataSourceUsed)
            {
                IMutationEngine mutationEngine = new CosmosMutationEngine(_cosmosClientProvider, _metadataProviderFactory, _authorizationResolver);
                _mutationEngines.Add(DatabaseType.CosmosDB_NoSQL, mutationEngine);
            }
        }

        public void OnConfigChanged(object? sender, HotReloadEventArgs args)
        {
            _mutationEngines = new Dictionary<DatabaseType, IMutationEngine>();
            ConfigureMutationEngines();
        }

        /// <inheritdoc/>
        public IMutationEngine GetMutationEngine(DatabaseType databaseType)
        {
            if (!_mutationEngines.TryGetValue(databaseType, out IMutationEngine? mutationEngine))
            {
                throw new DataApiBuilderException(
                    $"{nameof(databaseType)}:{databaseType} could not be found within the config",
                    HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            };

            return mutationEngine;
        }
    }
}
