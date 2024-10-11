// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using static Azure.DataApiBuilder.Config.DabConfigEvents;

namespace Azure.DataApiBuilder.Core.Resolvers.Factories
{
    /// <summary>
    /// QueryEngineFactory class.
    /// Used to get the appropriate queryEngine based on database type.
    /// </summary>
    public class QueryEngineFactory : IQueryEngineFactory
    {
        // Internally mutated during Hot-Reload
        private Dictionary<DatabaseType, IQueryEngine> _queryEngines;
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly IAbstractQueryManagerFactory _queryManagerFactory;
        private readonly IMetadataProviderFactory _metadataProviderFactory;
        private readonly CosmosClientProvider _cosmosClientProvider;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IAuthorizationResolver _authorizationResolver;
        private readonly GQLFilterParser _gQLFilterParser;
        private readonly DabCacheService _cache;
        private readonly ILogger<IQueryEngine> _logger;

        /// <inheritdoc/>
        public QueryEngineFactory(RuntimeConfigProvider runtimeConfigProvider,
            IAbstractQueryManagerFactory queryManagerFactory,
            IMetadataProviderFactory metadataProviderFactory,
            CosmosClientProvider cosmosClientProvider,
            IHttpContextAccessor contextAccessor,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            ILogger<IQueryEngine> logger,
            DabCacheService cache,
            HotReloadEventHandler<HotReloadEventArgs>? handler)
        {
            handler?.Subscribe(QUERY_ENGINE_FACTORY_ON_CONFIG_CHANGED, OnConfigChanged);
            _queryEngines = new Dictionary<DatabaseType, IQueryEngine>();
            _runtimeConfigProvider = runtimeConfigProvider;
            _queryManagerFactory = queryManagerFactory;
            _metadataProviderFactory = metadataProviderFactory;
            _cosmosClientProvider = cosmosClientProvider;
            _contextAccessor = contextAccessor;
            _authorizationResolver = authorizationResolver;
            _gQLFilterParser = gQLFilterParser;
            _cache = cache;
            _logger = logger;

            ConfigureQueryEngines();
        }

        public void ConfigureQueryEngines()
        {
            RuntimeConfig config = _runtimeConfigProvider.GetConfig();

            if (config.SqlDataSourceUsed)
            {
                IQueryEngine queryEngine = new SqlQueryEngine(
                    _queryManagerFactory,
                    _metadataProviderFactory,
                    _contextAccessor,
                    _authorizationResolver,
                    _gQLFilterParser,
                    _logger,
                    _runtimeConfigProvider,
                    _cache);
                _queryEngines.Add(DatabaseType.MSSQL, queryEngine);
                _queryEngines.Add(DatabaseType.MySQL, queryEngine);
                _queryEngines.Add(DatabaseType.PostgreSQL, queryEngine);
                _queryEngines.Add(DatabaseType.DWSQL, queryEngine);
            }

            if (config.CosmosDataSourceUsed)
            {
                IQueryEngine queryEngine = new CosmosQueryEngine(_cosmosClientProvider, _metadataProviderFactory, _authorizationResolver, _gQLFilterParser, _runtimeConfigProvider, _cache);
                _queryEngines.Add(DatabaseType.CosmosDB_NoSQL, queryEngine);
            }
        }

        public void OnConfigChanged(object? sender, HotReloadEventArgs args)
        {
            _queryEngines = new Dictionary<DatabaseType, IQueryEngine>();
            ConfigureQueryEngines();
        }

        /// <inheritdoc/>
        public IQueryEngine GetQueryEngine(DatabaseType databaseType)
        {
            if (!_queryEngines.TryGetValue(databaseType, out IQueryEngine? queryEngine))
            {
                throw new DataApiBuilderException(
                    $"{nameof(databaseType)}:{databaseType} could not be found within the config",
                    HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            return queryEngine;
        }
    }
}
