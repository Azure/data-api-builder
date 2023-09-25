// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Resolvers.Factories
{
    /// <summary>
    /// QueryEngineFactory class.
    /// Used to get the appropriate queryEngine based on database type.
    /// </summary>
    public class QueryEngineFactory : IQueryEngineFactory
    {
        private readonly Dictionary<DatabaseType, IQueryEngine> _queryEngines;

        /// <inheritdoc/>
        public QueryEngineFactory(RuntimeConfigProvider runtimeConfigProvider,
            IAbstractQueryManagerFactory queryManagerFactory,
            IMetadataProviderFactory metadataProviderFactory,
            CosmosClientProvider cosmosClientProvider,
            IHttpContextAccessor contextAccessor,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            ILogger<IQueryEngine> logger)
        {
            _queryEngines = new Dictionary<DatabaseType, IQueryEngine>();

            RuntimeConfig config = runtimeConfigProvider.GetConfig();

            if (config.SqlDataSourceUsed)
            {
                IQueryEngine queryEngine = new SqlQueryEngine(
                    queryManagerFactory, metadataProviderFactory, contextAccessor, authorizationResolver, gQLFilterParser, logger, runtimeConfigProvider);
                _queryEngines.Add(DatabaseType.MSSQL, queryEngine);
                _queryEngines.Add(DatabaseType.MySQL, queryEngine);
                _queryEngines.Add(DatabaseType.PostgreSQL, queryEngine);
            }

            if (config.CosmosDataSourceUsed)
            {
                IQueryEngine queryEngine = new CosmosQueryEngine(cosmosClientProvider, metadataProviderFactory, authorizationResolver, gQLFilterParser);
                _queryEngines.Add(DatabaseType.CosmosDB_NoSQL, queryEngine);
            }

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
